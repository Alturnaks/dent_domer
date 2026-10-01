$(function () {
    var inv = window.dentalInv, l = inv.l, esc = inv.esc, catalog = inv.catalog;
    var canManage = $('#InvSettings').data('can-manage') === true;
    var warehouses = [], categories = [], reasons = [], branches = [];

    function btns(kind, id) {
        if (!canManage) { return ''; }
        return '<button class="btn btn-sm btn-outline-secondary me-1" data-edit-' + kind + '="' + id + '"><i class="fa fa-pen"></i></button>' +
            '<button class="btn btn-sm btn-outline-danger" data-delete-' + kind + '="' + id + '"><i class="fa fa-trash"></i></button>';
    }

    function loadWarehouses() {
        return catalog.getWarehouses({}).then(function (r) {
            warehouses = r.items;
            var $tb = $('#WarehousesTable tbody').empty();
            warehouses.forEach(function (w) {
                $tb.append('<tr><td>' + esc(w.name) + '</td><td>' + esc(inv.enumText('WarehouseType', w.type)) + '</td><td>' + esc(w.branchName || '') + '</td><td>' +
                    (w.isLocked ? '<span class="badge bg-warning text-dark">' + esc(l('Inv:Locked')) + '</span>' : '') + '</td><td class="text-end text-nowrap">' + btns('wh', w.id) + '</td></tr>');
            });
        });
    }

    function loadCategories() {
        return catalog.getCategories().then(function (r) {
            categories = r.items;
            var $tb = $('#CategoriesTable tbody').empty();
            inv.categoryTree(categories).forEach(function (c) {
                $tb.append('<tr><td style="padding-left:' + (0.5 + c.depth * 1.5) + 'rem">' + (c.depth ? '<i class="fa fa-angle-right text-muted me-1"></i>' : '') + esc(c.name) +
                    '</td><td class="text-end">' + (c.itemsCount || '') + '</td><td class="text-end text-nowrap">' + btns('cat', c.id) + '</td></tr>');
            });
        });
    }

    function loadReasons() {
        return catalog.getWriteoffReasons().then(function (r) {
            reasons = r.items;
            var $tb = $('#ReasonsTable tbody').empty();
            reasons.forEach(function (x) {
                $tb.append('<tr><td>' + esc(x.name) + '</td><td>' + esc(inv.enumText('WriteoffReasonType', x.type)) + '</td><td class="text-end text-nowrap">' + btns('rs', x.id) + '</td></tr>');
            });
        });
    }

    dental.branches.branch.getLookup().then(function (r) { branches = r.items; });
    loadWarehouses(); loadCategories(); loadReasons();

    function warehouseForm(w) {
        w = w || { type: 1 };
        var typeOpts = [0, 1, 2].map(function (t) { return '<option value="' + t + '">' + esc(inv.enumText('WarehouseType', t)) + '</option>'; }).join('');
        var fields = [{ name: 'name', label: l('Inv:Name'), required: true, value: w.name }];
        if (!w.id) {
            fields.push({ name: 'type', label: l('Inv:WarehouseType'), type: 'select', options: typeOpts, value: w.type, col: 6 });
            fields.push({ name: 'branchId', label: l('Inv:Branch'), type: 'select', options: inv.options(branches, 'id', function (b) { return b.name; }, '—'), value: w.branchId, col: 6 });
        }
        fields.push({ name: 'parentWarehouseId', label: l('Inv:Parent'), type: 'select', options: inv.options(warehouses.filter(function (x) { return x.id !== w.id; }), 'id', function (x) { return x.name; }, '—'), value: w.parentWarehouseId });
        return inv.formModal({
            title: l('Inv:Warehouse'), fields: fields,
            collect: function ($m, v) { v.type = v.type === undefined ? w.type : Number(v.type); if (!w.id) { return; } v.branchId = w.branchId; },
            submit: function (v) { return w.id ? catalog.updateWarehouse(w.id, v) : catalog.createWarehouse(v); }
        }).then(loadWarehouses);
    }

    function categoryForm(c) {
        c = c || {};
        return inv.formModal({
            title: l('Inv:Category'),
            fields: [
                { name: 'name', label: l('Inv:Name'), required: true, value: c.name },
                { name: 'parentId', label: l('Inv:Parent'), type: 'select', options: inv.categoryOptions(categories.filter(function (x) { return x.id !== c.id; }), l('Inv:Root')), value: c.parentId }
            ],
            submit: function (v) { return c.id ? catalog.updateCategory(c.id, v) : catalog.createCategory(v); }
        }).then(loadCategories);
    }

    function reasonForm(r) {
        r = r || { type: 4 };
        var typeOpts = [0, 1, 2, 3, 4].map(function (t) { return '<option value="' + t + '">' + esc(inv.enumText('WriteoffReasonType', t)) + '</option>'; }).join('');
        return inv.formModal({
            title: l('Inv:Reason'),
            fields: [
                { name: 'name', label: l('Inv:Name'), required: true, value: r.name },
                { name: 'type', label: l('Inv:Type'), type: 'select', options: typeOpts, value: r.type }
            ],
            collect: function ($m, v) { v.type = Number(v.type); },
            submit: function (v) { return r.id ? catalog.updateWriteoffReason(r.id, v) : catalog.createWriteoffReason(v); }
        }).then(loadReasons);
    }

    function find(list, id) { return list.find(function (x) { return x.id === id; }); }
    function confirmDelete(name, fn, reload) {
        abp.message.confirm(l('Inv:DeleteConfirm', name)).then(function (ok) { if (ok) { fn().then(function () { abp.notify.info(l('SuccessfullyDeleted')); reload(); }); } });
    }

    $('#AddWarehouse').on('click', function () { warehouseForm(null); });
    $('#AddCategory').on('click', function () { categoryForm(null); });
    $('#AddReason').on('click', function () { reasonForm(null); });
    $(document).on('click', '[data-edit-wh]', function () { warehouseForm(find(warehouses, $(this).data('edit-wh'))); });
    $(document).on('click', '[data-edit-cat]', function () { categoryForm(find(categories, $(this).data('edit-cat'))); });
    $(document).on('click', '[data-edit-rs]', function () { reasonForm(find(reasons, $(this).data('edit-rs'))); });
    $(document).on('click', '[data-delete-wh]', function () { var w = find(warehouses, $(this).data('delete-wh')); confirmDelete(w.name, function () { return catalog.deleteWarehouse(w.id); }, loadWarehouses); });
    $(document).on('click', '[data-delete-cat]', function () { var c = find(categories, $(this).data('delete-cat')); confirmDelete(c.name, function () { return catalog.deleteCategory(c.id); }, loadCategories); });
    $(document).on('click', '[data-delete-rs]', function () { var r = find(reasons, $(this).data('delete-rs')); confirmDelete(r.name, function () { return catalog.deleteWriteoffReason(r.id); }, loadReasons); });
});
