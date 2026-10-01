/* Форма товара (создание/редактирование) с альтернативными единицами. */
(function () {
    var inv = window.dentalInv, l = inv.l, esc = inv.esc;

    function unitRow(u) {
        return '<tr><td><input class="form-control form-control-sm" data-u="unitName" value="' + esc(u.unitName) + '"><input type="hidden" data-u="id" value="' + esc(u.id || '') + '"></td>' +
            '<td><input type="number" step="any" min="0.0001" class="form-control form-control-sm" data-u="factorToBase" value="' + (u.factorToBase ?? 1) + '"></td>' +
            '<td class="text-end"><button type="button" class="btn btn-sm btn-outline-danger" data-u-remove><i class="fa fa-times"></i></button></td></tr>';
    }

    /** Открывает форму; item — ItemDto или null. Возвращает Promise<ItemDto>. */
    inv.openItemForm = function (item) {
        return inv.categories().then(function (categories) {
            var it = item || { baseUnit: 0, isActive: true, units: [] };
            var unitOptions = [0, 1, 2, 3].map(function (u) { return '<option value="' + u + '">' + esc(inv.unitText(u)) + '</option>'; }).join('');
            return inv.formModal({
                title: item ? l('Inv:EditItem') + ' ' + item.sku : l('Inv:NewItem'),
                size: 'modal-lg',
                fields: [
                    { name: 'sku', label: l('Inv:Sku'), required: true, value: it.sku, col: 3 },
                    { name: 'name', label: l('Inv:Name'), required: true, value: it.name, col: 9 },
                    { name: 'categoryId', label: l('Inv:Category'), type: 'select', options: inv.categoryOptions(categories, '—'), value: it.categoryId, col: 6 },
                    { name: 'manufacturer', label: l('Inv:Manufacturer'), value: it.manufacturer, col: 6 },
                    { name: 'baseUnit', label: l('Inv:BaseUnit'), type: 'select', options: unitOptions, value: it.baseUnit, col: 3 },
                    { name: 'barcode', label: l('Inv:Barcode'), value: it.barcode, col: 5 },
                    { name: 'isActive', label: l('Inv:IsActive'), type: 'checkbox', value: it.isActive, col: 4 },
                    { name: 'trackBatches', label: l('Inv:TrackBatches'), type: 'checkbox', value: it.trackBatches, col: 4 },
                    { name: 'trackExpiry', label: l('Inv:TrackExpiry'), type: 'checkbox', value: it.trackExpiry, col: 4 },
                    { name: 'trackSerials', label: l('Inv:TrackSerials'), type: 'checkbox', value: it.trackSerials, col: 4 }
                ],
                extraHtml: '<h6 class="mt-2">' + esc(l('Inv:Units')) + '</h6><table class="table table-sm" id="UnitsTable"><thead><tr><th>' + esc(l('Inv:UnitName')) + '</th><th style="width:160px">' +
                    esc(l('Inv:Factor')) + '</th><th></th></tr></thead><tbody>' + (it.units || []).map(unitRow).join('') + '</tbody></table>' +
                    '<button type="button" class="btn btn-sm btn-outline-primary" id="AddUnit"><i class="fa fa-plus me-1"></i>' + esc(l('Add')) + '</button>',
                onRender: function ($m) {
                    $m.on('click', '#AddUnit', function () { $m.find('#UnitsTable tbody').append(unitRow({ unitName: '', factorToBase: 1 })); });
                    $m.on('click', '[data-u-remove]', function () { $(this).closest('tr').remove(); });
                },
                collect: function ($m, v) {
                    v.baseUnit = Number(v.baseUnit);
                    v.units = [];
                    $m.find('#UnitsTable tbody tr').each(function () {
                        var name = $(this).find('[data-u="unitName"]').val();
                        if (!name) { return; }
                        v.units.push({ id: $(this).find('[data-u="id"]').val() || null, unitName: name, factorToBase: inv.num($(this).find('[data-u="factorToBase"]').val()) || 1 });
                    });
                },
                submit: function (v) { return item ? inv.catalog.updateItem(item.id, v) : inv.catalog.createItem(v); }
            });
        });
    };
})();
