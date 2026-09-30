$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.roles.roleLimit;
    var editModal = new abp.ModalManager(abp.appPath + 'RoleLimits/EditModal');

    function money(v) {
        return v === null || v === undefined ? '<span class="text-muted">' + l('Unlimited') + '</span>'
            : (v / 100).toLocaleString('ru-RU') + ' ₸';
    }

    var dataTable = $('#RoleLimitsTable').DataTable(
        abp.libs.datatables.normalizeConfiguration({
            serverSide: false,
            paging: false,
            ordering: false,
            searching: false,
            ajax: abp.libs.datatables.createAjax(service.getList),
            columnDefs: [
                {
                    title: l('Actions'),
                    rowAction: {
                        items: [{
                            text: l('Edit'),
                            action: function (data) { editModal.open({ roleId: data.record.roleId }); }
                        }]
                    }
                },
                { title: l('Employee:Role'), data: 'roleName' },
                {
                    title: l('RoleLimits:MaxDiscountPct'), data: 'maxDiscountPct',
                    render: function (v) { return v === null || v === undefined ? '<span class="text-muted">' + l('Unlimited') + '</span>' : v + '%'; }
                },
                { title: l('RoleLimits:MaxWriteoffAmount'), data: 'maxWriteoffAmount', render: money },
                { title: l('RoleLimits:MaxRefundAmount'), data: 'maxRefundAmount', render: money },
                {
                    title: l('RoleLimits:CanEditClosedShiftVisits'), data: 'canEditClosedShiftVisits',
                    render: function (v) { return v ? '<i class="fa fa-check text-success"></i>' : '<i class="fa fa-times text-muted"></i>'; }
                }
            ]
        })
    );

    editModal.onResult(function () { dataTable.ajax.reload(); });
});
