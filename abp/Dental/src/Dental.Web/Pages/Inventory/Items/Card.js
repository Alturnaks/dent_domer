$(function () {
    var inv = window.dentalInv, l = inv.l, esc = inv.esc;
    var $root = $('#ItemCard'), id = $root.data('id'), canManage = $root.data('can-manage') === true;
    var card = null;

    function load() {
        return Promise.all([inv.stock.getItemCard(id), inv.warehouses()]).then(function (r) {
            card = r[0];
            var it = card.item, unit = inv.unitText(it.baseUnit);
            $('#ItemTitle').text(it.name + ' · ' + it.sku);
            var info = [
                [l('Inv:Category'), it.categoryName], [l('Inv:Manufacturer'), it.manufacturer], [l('Inv:BaseUnit'), unit],
                [l('Inv:Units'), it.units.map(function (u) { return u.unitName + ' = ' + inv.qty(u.factorToBase) + ' ' + unit; }).join('; ')],
                [l('Inv:Barcode'), it.barcode],
                [l('Inv:Tracking'), [it.trackBatches ? l('Inv:Batch') : null, it.trackExpiry ? l('Inv:ExpiresAt') : null, it.trackSerials ? 'SN' : null].filter(Boolean).join(', ')],
                [l('Inv:TotalQty'), inv.qty(card.totalQty) + ' ' + unit + ' · ' + inv.money(card.totalAmount)]
            ];
            $('#ItemInfo').html(info.map(function (x) {
                return '<div class="col-md-3 mb-2"><div class="small text-muted">' + esc(x[0]) + '</div><div class="fw-semibold">' + (esc(x[1]) || '—') + '</div></div>';
            }).join('') + (it.isActive ? '' : '<div class="col-12"><span class="badge bg-secondary">' + esc(l('Inactive')) + '</span></div>'));

            var today = new Date().toISOString().substring(0, 10);
            var $b = $('#CardBalances tbody').empty();
            card.balances.forEach(function (b) {
                var exp = b.expiresAt ? inv.dateOnly(b.expiresAt) : '';
                if (b.expiresAt && b.expiresAt < today) { exp = '<span class="badge bg-danger">' + exp + '</span>'; }
                $b.append('<tr><td>' + esc(b.warehouseName) + '</td><td>' + esc(b.batchNumber || '') + (b.serialNumber ? ' <span class="text-muted">SN ' + esc(b.serialNumber) + '</span>' : '') +
                    '</td><td>' + exp + '</td><td class="text-end">' + inv.qty(b.qty) + ' ' + esc(unit) + '</td><td class="text-end">' + inv.money(b.unitCost) + '</td></tr>');
            });
            if (!card.balances.length) { $b.append('<tr><td colspan="5" class="text-muted text-center">—</td></tr>'); }

            var levels = {};
            card.levels.forEach(function (x) { levels[x.warehouseId] = x; });
            var $lv = $('#CardLevels tbody').empty();
            r[1].forEach(function (w) {
                var x = levels[w.id] || { minQty: 0, optimalQty: 0 };
                $lv.append('<tr data-wh="' + w.id + '"><td>' + esc(w.name) + '</td>' + (canManage
                    ? '<td><input type="number" step="any" min="0" class="form-control form-control-sm" data-min value="' + x.minQty + '"></td><td><input type="number" step="any" min="0" class="form-control form-control-sm" data-opt value="' + x.optimalQty + '"></td>'
                    : '<td>' + inv.qty(x.minQty) + '</td><td>' + inv.qty(x.optimalQty) + '</td>') + '</tr>');
            });

            var $mv = $('#CardMovements tbody').empty();
            card.movements.forEach(function (m) {
                $mv.append('<tr><td>' + inv.date(m.movedAt) + '</td><td><a href="' + abp.appPath + 'Inventory/Documents/Edit?id=' + m.documentId + '">' + esc(m.documentNumber) + '</a> <span class="text-muted small">' +
                    esc(inv.enumText('StockDocumentType', m.documentType)) + '</span></td><td>' + esc(m.warehouseName) + '</td><td>' + esc(m.batchNumber || '') + '</td><td class="text-end ' +
                    (m.qty < 0 ? 'text-danger' : 'text-success') + '">' + (m.qty > 0 ? '+' : '') + inv.qty(m.qty) + '</td><td class="text-end">' + inv.money(m.unitCost) + '</td></tr>');
            });
            if (!card.movements.length) { $mv.append('<tr><td colspan="6" class="text-muted text-center">—</td></tr>'); }
        });
    }

    load();

    $('#SaveLevels').on('click', function () {
        var levels = [];
        $('#CardLevels tbody tr').each(function () {
            levels.push({ warehouseId: $(this).data('wh'), minQty: inv.num($(this).find('[data-min]').val()) || 0, optimalQty: inv.num($(this).find('[data-opt]').val()) || 0 });
        });
        inv.catalog.setStockLevels(id, levels).then(function () { abp.notify.success(l('Inv:Saved')); load(); });
    });

    $('#EditItem').on('click', function () { inv.openItemForm(card.item).then(load); });
});
