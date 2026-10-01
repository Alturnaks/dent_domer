$(function () {
    var inv = window.dentalInv, l = inv.l, esc = inv.esc;

    function load() {
        var input = {
            warehouseId: $('#FWarehouse').val() || null,
            categoryId: $('#FCategory').val() || null,
            filter: $('#FFilter').val() || null,
            belowMin: $('#FBelowMin').is(':checked'),
            expiringDays: $('#FExpiring').val() ? Number($('#FExpiring').val()) : null
        };
        return inv.stock.getBalances(input).then(function (res) {
            var $tb = $('#BalancesTable tbody').empty();
            var total = 0;
            res.items.forEach(function (r) {
                total += r.amount;
                var flags = '';
                if (r.belowMin) { flags += ' <span class="badge bg-danger">' + esc(l('Inv:BelowMin')) + '</span>'; }
                var exp = r.nearestExpiry ? inv.dateOnly(r.nearestExpiry) : '';
                if (r.expired) { exp = '<span class="badge bg-danger">' + exp + ' · ' + esc(l('Inv:Expired')) + '</span>'; }
                else if (r.expiring) { exp = '<span class="badge bg-warning text-dark">' + exp + '</span>'; }
                var lv = r.minQty === null || r.minQty === undefined ? '' : inv.qty(r.minQty) + ' / ' + inv.qty(r.optimalQty);
                $tb.append('<tr' + (r.belowMin ? ' class="table-danger"' : '') + '>' +
                    '<td>' + esc(r.warehouseName) + '</td>' +
                    '<td class="text-muted">' + esc(r.itemSku) + '</td>' +
                    '<td><a href="' + abp.appPath + 'Inventory/Items/Card?id=' + r.itemId + '">' + esc(r.itemName) + '</a>' + flags + '</td>' +
                    '<td class="text-muted">' + esc(r.categoryName) + '</td>' +
                    '<td class="text-end">' + inv.qty(r.qty) + ' ' + esc(inv.unitText(r.baseUnit)) + '</td>' +
                    '<td class="text-end text-muted">' + lv + '</td>' +
                    '<td>' + exp + '</td>' +
                    '<td class="text-end">' + inv.money(r.avgCost) + '</td>' +
                    '<td class="text-end">' + inv.money(r.amount) + '</td></tr>');
            });
            if (!res.items.length) { $tb.append('<tr><td colspan="9" class="text-center text-muted">' + esc(l('NoDataAvailableInDatatable') || '—') + '</td></tr>'); }
            $('#BalancesTotal').text(inv.money(total));
        });
    }

    Promise.all([inv.warehouses(), inv.categories()]).then(function (r) {
        $('#FWarehouse').html(inv.options(r[0], 'id', function (w) { return w.name; }, l('Inv:AllWarehouses')));
        $('#FCategory').html(inv.categoryOptions(r[1], l('Inv:AllCategories')));
        load();
    });

    var timer;
    $('#FWarehouse, #FCategory, #FBelowMin, #FExpiring').on('change', load);
    $('#FFilter').on('input', function () { clearTimeout(timer); timer = setTimeout(load, 300); });

    $('#RebuildButton').on('click', function () {
        abp.message.confirm(l('Inv:RebuildConfirm')).then(function (ok) {
            if (!ok) { return; }
            inv.stock.rebuildBalances().then(function (n) { abp.notify.success(l('Inv:RebuildDone', n)); load(); });
        });
    });
});
