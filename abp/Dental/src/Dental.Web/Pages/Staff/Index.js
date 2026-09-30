$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.staff.employee;
    var createModal = new abp.ModalManager(abp.appPath + 'Staff/CreateModal');
    var editModal = new abp.ModalManager(abp.appPath + 'Staff/EditModal');

    function esc(s) { return $('<div>').text(s || '').html(); }

    var dataTable = $('#StaffTable').DataTable(
        abp.libs.datatables.normalizeConfiguration({
            serverSide: true,
            paging: true,
            ordering: false,
            searching: false,
            scrollX: true,
            ajax: abp.libs.datatables.createAjax(service.getList, function () {
                var active = $('#StaffActiveFilter').val();
                return { filter: $('#StaffFilter').val(), isActive: active === '' ? null : active === 'true' };
            }),
            columnDefs: [
                {
                    title: l('Actions'),
                    rowAction: {
                        items: [
                            {
                                text: l('Edit'),
                                action: function (data) { editModal.open({ id: data.record.id }); }
                            },
                            {
                                text: l('Employee:Fire'),
                                visible: function (record) { return record.isActive; },
                                confirmMessage: function (data) { return l('Employee:FireConfirm', data.record.fullName); },
                                action: function (data) {
                                    service.fire(data.record.id).then(function () { abp.notify.info(l('Employee:Fired')); dataTable.ajax.reload(); });
                                }
                            },
                            {
                                text: l('Employee:Rehire'),
                                visible: function (record) { return !record.isActive; },
                                action: function (data) {
                                    service.rehire(data.record.id).then(function () { dataTable.ajax.reload(); });
                                }
                            }
                        ]
                    }
                },
                {
                    title: l('Employee:FullName'), data: 'fullName',
                    render: function (v, t, r) {
                        var dot = r.color ? '<span class="d-inline-block rounded-circle me-2" style="width:10px;height:10px;background:' + esc(r.color) + '"></span>' : '';
                        return dot + esc(v) + '<div class="small text-muted">' + esc(r.email) + '</div>';
                    }
                },
                { title: l('Employee:Position'), data: 'position', render: function (v) { return l('Enum:StaffPosition.' + v); } },
                { title: l('Employee:Role'), data: 'roleNames', render: function (v) { return esc((v || []).join(', ')); } },
                { title: l('Employee:Specialty'), data: 'specialty', render: function (v) { return esc(v); } },
                {
                    title: l('Employee:Branches'), data: 'branchNames',
                    render: function (v, t, r) { return r.allBranches ? l('AllBranches') : esc((v || []).join(', ')); }
                },
                { title: l('Employee:Phone'), data: 'phone', render: function (v) { return esc(v); } },
                {
                    title: l('Employee:Status'), data: 'isActive',
                    render: function (v, t, r) {
                        return v ? '<span class="badge bg-success">' + l('Employee:Active') + '</span>'
                            : '<span class="badge bg-secondary">' + l('Employee:FiredAt') + ' ' + (r.firedAt ? luxon.DateTime.fromISO(r.firedAt).toLocaleString() : '') + '</span>';
                    }
                }
            ]
        })
    );

    createModal.onResult(function () { dataTable.ajax.reload(); });
    editModal.onResult(function () { dataTable.ajax.reload(); });

    $('#NewEmployeeButton').click(function (e) { e.preventDefault(); createModal.open(); });
    $('#StaffFilter').on('input', abp.utils.debounce ? abp.utils.debounce(function () { dataTable.ajax.reload(); }, 300) : function () { dataTable.ajax.reload(); });
    $('#StaffActiveFilter').change(function () { dataTable.ajax.reload(); });
});
