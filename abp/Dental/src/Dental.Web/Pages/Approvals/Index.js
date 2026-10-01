$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.approvals.approval;
    var esc = dentalUi.esc;
    var statusNames = ['Pending', 'Approved', 'Rejected'];
    var types = ['Discount', 'Writeoff', 'Refund', 'ClosedVisitEdit', 'PurchaseOrder', 'TransferShortage', 'PayrollPeriodChange'];
    var status = 0;

    types.forEach(function (t) { $('#ApprovalTypeFilter').append($('<option>').val(t).text(l('ApprovalType:' + t))); });

    function amount(r) {
        if (r.type === 'Discount') { return r.amount + ' %'; }
        if (r.type === 'ClosedVisitEdit' || r.type === 'PayrollPeriodChange') { return ''; }
        return dentalUi.money(r.amount);
    }

    var dataTable = $('#ApprovalsTable').DataTable(
        abp.libs.datatables.normalizeConfiguration({
            serverSide: true,
            paging: true,
            ordering: false,
            searching: false,
            scrollX: true,
            ajax: abp.libs.datatables.createAjax(service.getList, function () {
                return { status: status === '' ? null : status, type: $('#ApprovalTypeFilter').val() || null };
            }),
            columnDefs: [
                {
                    title: l('Actions'),
                    rowAction: {
                        items: [
                            { text: l('Approval:Approve'), visible: function (r) { return r.canDecide; }, action: function (d) { openDecision(d.record, true); } },
                            { text: l('Approval:Reject'), visible: function (r) { return r.canDecide; }, action: function (d) { openDecision(d.record, false); } },
                            { text: l('Approval:Details'), action: function (d) { openDecision(d.record, null); } }
                        ]
                    }
                },
                { title: l('Approval:Requested'), data: 'creationTime', render: function (v) { return dentalUi.dateTime(v); } },
                { title: l('Type'), data: 'typeTitle', render: function (v) { return esc(v); } },
                { title: l('Approval:Summary'), data: 'summary', render: function (v) { return esc(v); } },
                { title: l('Approval:Amount'), data: 'amount', className: 'text-end text-nowrap', render: function (v, t, r) { return amount(r); } },
                { title: l('Approval:Branch'), data: 'branchName', render: function (v) { return esc(v || l('AllBranches')); } },
                { title: l('Approval:RequestedBy'), data: 'requestedByName', render: function (v) { return esc(v); } },
                {
                    title: l('Status'), data: 'status',
                    render: function (v, t, r) {
                        var name = dentalUi.enumName(v, statusNames);
                        var cls = name === 'Approved' ? 'bg-success' : name === 'Rejected' ? 'bg-danger' : 'bg-warning text-dark';
                        var html = '<span class="badge ' + cls + '">' + esc(l('Enum:ApprovalStatus.' + name)) + '</span>';
                        if (name === 'Pending') {
                            html += '<div class="small text-muted">' + esc(l('Approval:WhoCanDecide')) + ': ' + esc(r.decidersHint) + '</div>';
                            if (!r.canDecide) { html += '<div class="small text-muted fst-italic">' + esc(l('Approval:CannotDecide')) + '</div>'; }
                        } else {
                            html += '<div class="small text-muted">' + esc(r.decidedByName || '') + ' · ' + dentalUi.dateTime(r.decidedAt) + '</div>';
                            if (r.comment) { html += '<div class="small">' + esc(r.comment) + '</div>'; }
                        }
                        return html;
                    }
                }
            ]
        })
    );

    function refreshCount() {
        service.getPendingCount().then(function (c) { $('#PendingCount').text(c).toggleClass('d-none', !c); });
    }
    refreshCount();

    $('.nav-tabs [data-status]').on('click', function () {
        $('.nav-tabs .nav-link').removeClass('active');
        $(this).addClass('active');
        var s = $(this).data('status');
        status = s === '' ? '' : parseInt(s, 10);
        dataTable.ajax.reload();
    });
    $('#ApprovalTypeFilter').on('change', function () { dataTable.ajax.reload(); });

    // Модалка решения / подробностей
    var $modal = $('#ApprovalDecisionModal');
    var modal = bootstrap.Modal.getOrCreateInstance($modal[0]);
    var current = null, approve = null;

    function openDecision(r, isApprove) {
        current = r;
        approve = isApprove;
        $modal.find('[data-role=title]').text(r.typeTitle);
        $modal.find('[data-role=summary]').html(esc(r.summary) + (amount(r) ? ' — <strong>' + amount(r) + '</strong>' : '') +
            '<div class="small text-muted">' + esc(r.requestedByName) + ', ' + dentalUi.dateTime(r.creationTime) + '</div>');
        var payload = r.payload;
        try { payload = JSON.stringify(JSON.parse(r.payload), null, 2); } catch (e) { /* как есть */ }
        $modal.find('[data-role=payload]').text(payload);
        $modal.find('[name=comment]').val(r.comment || '').prop('disabled', isApprove === null);
        var $btn = $modal.find('[data-role=submit]');
        $btn.toggleClass('d-none', isApprove === null)
            .removeClass('btn-success btn-danger').addClass(isApprove ? 'btn-success' : 'btn-danger')
            .text(isApprove ? l('Approval:Approve') : l('Approval:Reject'));
        modal.show();
    }

    $modal.find('form').on('submit', function (e) {
        e.preventDefault();
        if (!current || approve === null) { return; }
        var input = { comment: $modal.find('[name=comment]').val().trim() || null };
        (approve ? service.approve(current.id, input) : service.reject(current.id, input)).then(function () {
            modal.hide();
            abp.notify.success(approve ? l('Approval:Approved') : l('Approval:Rejected'));
            dataTable.ajax.reload(null, false);
            refreshCount();
        });
    });
});
