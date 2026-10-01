$(function () {
    var inv = window.dentalInv, l = inv.l, esc = inv.esc;
    var canManage = $('#ItemsFilters').data('can-manage') === true;

    inv.categories().then(function (c) { $('#FCategory').html(inv.categoryOptions(c, l('Inv:AllCategories'))); });

    var table = $('#ItemsTable').DataTable(abp.libs.datatables.normalizeConfiguration({
        serverSide: true,
        paging: true,
        searching: false,
        order: [[2, 'asc']],
        scrollX: true,
        ajax: abp.libs.datatables.createAjax(inv.catalog.getItems, function () {
            return { filter: $('#FFilter').val() || null, categoryId: $('#FCategory').val() || null, includeInactive: $('#FInactive').is(':checked') };
        }),
        columnDefs: [
            {
                title: l('Actions'),
                rowAction: {
                    items: [
                        { text: l('Inv:Card'), action: function (d) { window.location.href = abp.appPath + 'Inventory/Items/Card?id=' + d.record.id; } },
                        {
                            text: l('Edit'), visible: function () { return canManage; },
                            action: function (d) { inv.openItemForm(d.record).then(function () { table.ajax.reload(); }); }
                        }
                    ]
                }
            },
            { title: l('Inv:Sku'), data: 'sku' },
            {
                title: l('Inv:Name'), data: 'name',
                render: function (v, t, r) {
                    return '<a href="' + abp.appPath + 'Inventory/Items/Card?id=' + r.id + '">' + esc(v) + '</a>' +
                        (r.isActive ? '' : ' <span class="badge bg-secondary">' + esc(l('Inactive')) + '</span>');
                }
            },
            { title: l('Inv:Category'), data: 'categoryName', orderable: false, render: esc },
            { title: l('Inv:Manufacturer'), data: 'manufacturer', orderable: false, render: esc },
            {
                title: l('Inv:BaseUnit'), data: 'baseUnit', orderable: false,
                render: function (v, t, r) {
                    return esc(inv.unitText(v)) + (r.units.length ? '<div class="small text-muted">' + r.units.map(function (u) { return esc(u.unitName) + ' = ' + inv.qty(u.factorToBase); }).join('; ') + '</div>' : '');
                }
            },
            {
                title: l('Inv:Tracking'), data: 'trackBatches', orderable: false,
                render: function (v, t, r) {
                    var b = [];
                    if (r.trackBatches) { b.push(l('Inv:Batch')); }
                    if (r.trackExpiry) { b.push(l('Inv:ExpiresAt')); }
                    if (r.trackSerials) { b.push('SN'); }
                    return b.map(function (x) { return '<span class="badge bg-light text-dark border me-1">' + esc(x) + '</span>'; }).join('');
                }
            }
        ]
    }));

    var timer;
    $('#FCategory, #FInactive').on('change', function () { table.ajax.reload(); });
    $('#FFilter').on('input', function () { clearTimeout(timer); timer = setTimeout(function () { table.ajax.reload(); }, 300); });
    $('#NewItemButton').on('click', function (e) {
        e.preventDefault();
        inv.openItemForm(null).then(function (it) { window.location.href = abp.appPath + 'Inventory/Items/Card?id=' + it.id; });
    });
});
