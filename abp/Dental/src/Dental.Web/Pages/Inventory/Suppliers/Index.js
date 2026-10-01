$(function () {
    var inv = window.dentalInv, l = inv.l, esc = inv.esc;
    var canManage = $('#SupplierFilters').data('can-manage') === true;

    function openForm(s) {
        s = s || { paymentTermsDays: 0 };
        return inv.formModal({
            title: s.id ? s.name : l('Inv:Supplier'),
            size: 'modal-lg',
            fields: [
                { name: 'name', label: l('Inv:Name'), required: true, value: s.name, col: 8 },
                { name: 'bin', label: l('Inv:Bin'), value: s.bin, col: 4 },
                { name: 'contactPerson', label: l('Inv:Contact'), value: s.contactPerson, col: 6 },
                { name: 'phone', label: l('Inv:Phone'), value: s.phone, col: 6 },
                { name: 'email', label: l('Inv:Email'), value: s.email, col: 6 },
                { name: 'whatsapp', label: l('Inv:Whatsapp'), value: s.whatsapp, col: 3 },
                { name: 'paymentTermsDays', label: l('Inv:PaymentTerms'), type: 'number', value: s.paymentTermsDays, col: 3 },
                { name: 'notes', label: l('Inv:Notes'), type: 'textarea', value: s.notes }
            ],
            collect: function ($m, v) { v.paymentTermsDays = v.paymentTermsDays || 0; },
            submit: function (v) { return s.id ? inv.catalog.updateSupplier(s.id, v) : inv.catalog.createSupplier(v); }
        });
    }

    function priceRow(r, items) {
        var itemCell = r.itemId
            ? esc(r.itemName) + ' <span class="text-muted small">' + esc(r.itemSku) + '</span><input type="hidden" data-p="itemId" value="' + r.itemId + '">'
            : '<select class="form-select form-select-sm" data-p="itemId"><option value=""></option>' + items.map(function (i) { return '<option value="' + i.id + '">' + esc(i.name + ' · ' + i.sku) + '</option>'; }).join('') + '</select>';
        return '<tr><td>' + itemCell + '</td><td><input class="form-control form-control-sm" data-p="supplierSku" value="' + esc(r.supplierSku) + '"' + (canManage ? '' : ' disabled') + '></td>' +
            '<td><input type="number" step="0.01" min="0" class="form-control form-control-sm" data-p="price" value="' + (r.lastPrice !== undefined ? r.lastPrice / 100 : '') + '"' + (canManage ? '' : ' disabled') + '></td>' +
            '<td class="small text-muted">' + (r.lastPriceAt ? inv.dateOnly(r.lastPriceAt) : '') + '</td></tr>';
    }

    function openPrices(s) {
        Promise.all([inv.catalog.getSupplierItems(s.id), inv.items()]).then(function (r) {
            var rows = r[0].items, items = r[1];
            inv.formModal({
                title: l('Inv:Prices') + ' — ' + s.name,
                size: 'modal-xl',
                okText: canManage ? l('Save') : l('Close'),
                fields: [],
                extraHtml: '<div style="max-height:60vh;overflow:auto"><table class="table table-sm" id="PricesTable"><thead><tr><th>' + esc(l('Inv:Item')) + '</th><th style="width:160px">' + esc(l('Inv:SupplierSku')) +
                    '</th><th style="width:150px">' + esc(l('Inv:LastPrice')) + '</th><th style="width:110px">' + esc(l('Inv:LastPriceAt')) + '</th></tr></thead><tbody>' +
                    rows.map(function (x) { return priceRow(x, items); }).join('') + '</tbody></table></div>' +
                    (canManage ? '<button type="button" class="btn btn-sm btn-outline-primary" id="AddPrice"><i class="fa fa-plus me-1"></i>' + esc(l('Inv:AddItem')) + '</button>' : ''),
                onRender: function ($m) { $m.on('click', '#AddPrice', function () { $m.find('#PricesTable tbody').append(priceRow({}, items)); }); },
                collect: function ($m, v) {
                    v.items = [];
                    $m.find('#PricesTable tbody tr').each(function () {
                        var itemId = $(this).find('[data-p="itemId"]').val();
                        if (!itemId) { return; }
                        v.items.push({ itemId: itemId, supplierSku: $(this).find('[data-p="supplierSku"]').val() || null, lastPrice: inv.toTiyn($(this).find('[data-p="price"]').val()) || 0 });
                    });
                },
                submit: function (v) { return canManage ? inv.catalog.setSupplierItems(s.id, v.items) : null; }
            });
        });
    }

    var table = $('#SuppliersTable').DataTable(abp.libs.datatables.normalizeConfiguration({
        serverSide: true,
        paging: true,
        searching: false,
        ordering: false,
        scrollX: true,
        ajax: abp.libs.datatables.createAjax(inv.catalog.getSuppliers, function () { return { filter: $('#FFilter').val() || null }; }),
        columnDefs: [
            {
                title: l('Actions'),
                rowAction: {
                    items: [
                        { text: l('Inv:Prices'), action: function (d) { openPrices(d.record); } },
                        { text: l('Edit'), visible: function () { return canManage; }, action: function (d) { openForm(d.record).then(function () { table.ajax.reload(); }); } },
                        {
                            text: l('Delete'), visible: function () { return canManage; },
                            confirmMessage: function (d) { return l('Inv:DeleteConfirm', d.record.name); },
                            action: function (d) { inv.catalog.deleteSupplier(d.record.id).then(function () { table.ajax.reload(); }); }
                        }
                    ]
                }
            },
            { title: l('Inv:Name'), data: 'name', render: esc },
            { title: l('Inv:Bin'), data: 'bin', render: esc },
            { title: l('Inv:Contact'), data: 'contactPerson', render: esc },
            { title: l('Inv:Phone'), data: 'phone', render: esc },
            { title: l('Inv:Email'), data: 'email', render: esc },
            { title: l('Inv:PaymentTerms'), data: 'paymentTermsDays', className: 'text-end' },
            { title: l('Inv:Notes'), data: 'notes', render: function (v) { return '<span class="small text-muted">' + esc(v) + '</span>'; } }
        ]
    }));

    var timer;
    $('#FFilter').on('input', function () { clearTimeout(timer); timer = setTimeout(function () { table.ajax.reload(); }, 300); });
    $('#NewSupplierButton').on('click', function (e) { e.preventDefault(); openForm(null).then(function () { table.ajax.reload(); }); });
});
