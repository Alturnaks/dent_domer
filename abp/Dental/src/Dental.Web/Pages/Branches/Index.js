$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.branches.branch;
    var createModal = new abp.ModalManager(abp.appPath + 'Branches/CreateModal');
    var editModal = new abp.ModalManager(abp.appPath + 'Branches/EditModal');

    var dataTable = $('#BranchesTable').DataTable(
        abp.libs.datatables.normalizeConfiguration({
            serverSide: true,
            paging: true,
            order: [[1, 'asc']],
            searching: false,
            scrollX: true,
            ajax: abp.libs.datatables.createAjax(service.getList),
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
                                text: l('Branch:RoomsAndChairs'),
                                action: function (data) { window.location.href = abp.appPath + 'Branches/Details?id=' + data.record.id; }
                            },
                            {
                                text: l('Delete'),
                                confirmMessage: function (data) { return l('Branch:DeleteConfirm', data.record.name); },
                                action: function (data) {
                                    service.delete(data.record.id).then(function () {
                                        abp.notify.info(l('SuccessfullyDeleted'));
                                        dataTable.ajax.reload();
                                    });
                                }
                            }
                        ]
                    }
                },
                { title: l('Branch:Name'), data: 'name' },
                { title: l('Branch:Address'), data: 'address', orderable: false },
                { title: l('Branch:Phone'), data: 'phone', orderable: false },
                {
                    title: l('Branch:IsActive'), data: 'isActive',
                    render: function (v) { return v ? '<i class="fa fa-check text-success"></i>' : '<i class="fa fa-times text-muted"></i>'; }
                }
            ]
        })
    );

    createModal.onResult(function () { dataTable.ajax.reload(); });
    editModal.onResult(function () { dataTable.ajax.reload(); });

    $('#NewBranchButton').click(function (e) {
        e.preventDefault();
        createModal.open();
    });
});
