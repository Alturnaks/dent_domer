$(function () {
    var l = abp.localization.getResource('Dental');
    var esc = dentalUi.esc;
    var sources = dental.patients.leadSource;
    var reasons = dental.references.cancelReason;
    var reasonTypes = ['Cancel', 'Reschedule'];

    function row(item, extra) {
        return '<tr data-id="' + item.id + '"><td><span data-role="name">' + esc(item.name) + '</span></td><td>' + (extra || '') + '</td>' +
            '<td class="text-end text-nowrap"><button class="btn btn-sm btn-link" data-act="rename"><i class="fa fa-pen"></i></button>' +
            '<button class="btn btn-sm btn-link text-danger" data-act="delete"><i class="fa fa-trash"></i></button></td></tr>';
    }

    function loadSources() {
        sources.getList().then(function (r) { $('#LeadSourcesBody').html(r.items.map(function (x) { return row(x); }).join('')); });
    }
    function loadReasons() {
        reasons.getList(null).then(function (r) {
            $('#CancelReasonsBody').html(r.items.map(function (x) {
                return row(x, '<span class="badge bg-light text-dark border" data-type="' + dentalUi.enumName(x.type, reasonTypes) + '">' +
                    esc(dentalUi.enumText('CancelReasonType', x.type, reasonTypes)) + '</span>');
            }).join(''));
        });
    }

    function bind(body, svc, reload, getType) {
        $(body).on('click', 'button[data-act]', function () {
            var $tr = $(this).closest('tr'), id = $tr.data('id'), name = $tr.find('[data-role=name]').text();
            if ($(this).data('act') === 'delete') {
                abp.message.confirm(l('References:DeleteConfirm', name)).then(function (ok) {
                    if (ok) { svc.delete(id).then(reload); }
                });
            } else {
                var newName = window.prompt(l('Name'), name);
                if (newName && newName.trim() && newName.trim() !== name) {
                    var input = { name: newName.trim() };
                    if (getType) { input.type = getType($tr); }
                    svc.update(id, input).then(reload);
                }
            }
        });
    }
    bind('#LeadSourcesBody', sources, loadSources);
    bind('#CancelReasonsBody', reasons, loadReasons, function ($tr) { return reasonTypes.indexOf($tr.find('[data-type]').data('type')); });

    $('#LeadSourceForm').on('submit', function (e) {
        e.preventDefault();
        var $i = $(this).find('[name=name]');
        if (!$i.val().trim()) { return; }
        sources.create({ name: $i.val().trim() }).then(function () { $i.val(''); loadSources(); });
    });
    $('#CancelReasonForm').on('submit', function (e) {
        e.preventDefault();
        var $i = $(this).find('[name=name]');
        if (!$i.val().trim()) { return; }
        reasons.create({ name: $i.val().trim(), type: parseInt($(this).find('[name=type]').val(), 10) }).then(function () { $i.val(''); loadReasons(); });
    });

    loadSources();
    loadReasons();
});
