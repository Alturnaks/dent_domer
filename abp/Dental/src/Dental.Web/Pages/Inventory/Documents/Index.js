$(function () {
    var inv = window.dentalInv, l = inv.l, esc = inv.esc;

    inv.warehouses().then(function (list) {
        $('#FWarehouse').html(inv.options(list, 'id', function (w) { return w.name; }, l('Inv:AllWarehouses')));
    });

    var table = $('#DocumentsTable').DataTable(abp.libs.datatables.normalizeConfiguration({
        serverSide: true,
        paging: true,
        searching: false,
        ordering: false,
        scrollX: true,
        ajax: abp.libs.datatables.createAjax(inv.stock.getList, function () {
            return {
                type: $('#FType').val() === '' ? null : Number($('#FType').val()),
                status: $('#FStatus').val() === '' ? null : Number($('#FStatus').val()),
                warehouseId: $('#FWarehouse').val() || null,
                from: $('#FFrom').val() || null,
                to: $('#FTo').val() || null,
                filter: $('#FFilter').val() || null
            };
        }),
        columnDefs: [
            {
                title: l('Inv:Number'), data: 'number',
                render: function (v, t, r) { return '<a href="' + abp.appPath + 'Inventory/Documents/Edit?id=' + r.id + '">' + esc(v) + '</a>'; }
            },
            { title: l('Inv:Type'), data: 'type', render: function (v) { return esc(inv.enumText('StockDocumentType', v)); } },
            { title: l('Inv:Status'), data: 'status', render: function (v) { return inv.statusBadge(v); } },
            { title: l('Inv:WarehouseFrom'), data: 'warehouseFromName', render: esc },
            { title: l('Inv:WarehouseTo'), data: 'warehouseToName', render: esc },
            {
                title: l('Inv:Supplier') + ' / ' + l('Inv:Reason'), data: 'supplierName',
                render: function (v, t, r) { return esc(v || r.reasonName || ''); }
            },
            { title: l('Inv:Created'), data: 'creationTime', render: function (v) { return inv.date(v); } },
            { title: l('Inv:Lines'), data: 'linesCount', className: 'text-end' },
            { title: l('Inv:Amount'), data: 'totalCost', className: 'text-end', render: function (v) { return inv.money(v); } },
            { title: l('Inv:Comment'), data: 'comment', render: function (v) { return '<span class="text-muted small">' + esc((v || '').substring(0, 60)) + '</span>'; } }
        ]
    }));

    var timer;
    $('#FType, #FStatus, #FWarehouse, #FFrom, #FTo').on('change', function () { table.ajax.reload(); });
    $('#FFilter').on('input', function () { clearTimeout(timer); timer = setTimeout(function () { table.ajax.reload(); }, 300); });
});
