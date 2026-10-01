$(function () {
    var inv = window.dentalInv, l = inv.l, esc = inv.esc;
    var T = { Receipt: 0, Transfer: 1, Writeoff: 2, Inventory: 3, Return: 4, Visit: 5 };
    var S = { Draft: 0, Pending: 1, Posted: 2, InTransit: 3, Received: 4, Cancelled: 5 };
    var $root = $('#DocEditor');
    var perms = {
        receive: $root.data('perm-receive') === true, transferCreate: $root.data('perm-transfer-create') === true,
        transferReceive: $root.data('perm-transfer-receive') === true, writeoff: $root.data('perm-writeoff') === true,
        count: $root.data('perm-count') === true, countApprove: $root.data('perm-count-approve') === true
    };
    var docId = $root.data('id') || null;
    var refs = { items: [], itemsById: {}, warehouses: [], suppliers: [], reasons: [] };
    var doc = null;      // StockDocumentDto или черновик нового документа
    var lines = [];      // строки редактора

    function canEditType(type) {
        return type === T.Receipt ? perms.receive : type === T.Transfer ? perms.transferCreate
            : type === T.Writeoff || type === T.Return ? perms.writeoff : type === T.Inventory ? perms.count : false;
    }
    function canPostType(type) {
        return type === T.Inventory ? perms.countApprove : canEditType(type);
    }
    function isNew() { return !doc.id; }
    function editable() { return (isNew() || doc.status === S.Draft) && canEditType(doc.type); }
    function isOut(type) { return type === T.Writeoff || type === T.Transfer || type === T.Return; }

    function item(id) { return refs.itemsById[id]; }
    function factor(it, unitId) {
        if (!it || !unitId) { return 1; }
        var u = it.units.find(function (x) { return x.id === unitId; });
        return u ? u.factorToBase : 1;
    }

    // ================= Загрузка =================

    Promise.all([inv.items(), inv.warehouses(), inv.suppliers(), inv.reasons()]).then(function (r) {
        refs.items = r[0]; refs.warehouses = r[1]; refs.suppliers = r[2]; refs.reasons = r[3];
        refs.items.forEach(function (i) { refs.itemsById[i.id] = i; });
        if (docId) {
            loadDoc(docId);
        } else {
            doc = { id: null, type: Number($root.data('type') || 0), status: S.Draft, lines: [] };
            lines = [];
            if (doc.type !== T.Inventory) { lines.push(newLine()); }
            render();
        }
    });

    function loadDoc(id) {
        return inv.stock.get(id).then(function (d) {
            doc = d;
            lines = d.lines.map(function (x) {
                var it = item(x.itemId);
                var f = factor(it, x.unitId);
                return {
                    id: x.id, itemId: x.itemId, itemName: x.itemName, itemSku: x.itemSku, baseUnit: x.baseUnit,
                    qty: x.unitId ? x.qtyInput : x.qty, unitId: x.unitId, unitName: x.unitName,
                    price: x.unitCost ? Math.round(x.unitCost * f) / 100 : null,
                    unitCost: x.unitCost, totalCost: x.totalCost, batchId: x.batchId, batchNumber: x.batchNumber, serialNumber: x.serialNumber,
                    expiresAt: x.expiresAt, expectedQty: x.expectedQty, actualQty: x.actualQty, sentQty: x.qty
                };
            });
            render();
        });
    }

    function newLine() { return { itemId: null, qty: 1, unitId: null, price: null, batchId: null, batchNumber: null, serialNumber: null, expiresAt: null }; }

    // ================= Шапка =================

    function whName(id) { var w = refs.warehouses.find(function (x) { return x.id === id; }); return w ? w.name : (id ? '?' : ''); }

    function field(label, html, col) { return '<div class="col-md-' + (col || 3) + '"><label class="form-label">' + esc(label) + '</label>' + html + '</div>'; }
    function roField(label, text, col) { return field(label, '<div class="form-control-plaintext fw-semibold">' + (text || '—') + '</div>', col); }
    function sel(name, list, textFn, value, empty) {
        return '<select class="form-select" data-h="' + name + '">' + inv.options(list, 'id', textFn, empty === undefined ? '' : empty, value) + '</select>';
    }

    function renderHeader() {
        var t = doc.type, ed = editable(), h = '';
        var whFn = function (w) { return w.name; };
        var fromEditable = ed && (t !== T.Inventory || isNew());
        if (t === T.Receipt) {
            h += ed ? field(l('Inv:WarehouseTo'), sel('warehouseToId', refs.warehouses, whFn, doc.warehouseToId)) : roField(l('Inv:WarehouseTo'), esc(doc.warehouseToName));
            h += ed ? field(l('Inv:Supplier'), sel('supplierId', refs.suppliers, function (s) { return s.name; }, doc.supplierId)) : roField(l('Inv:Supplier'), esc(doc.supplierName));
            h += ed ? field(l('Inv:InvoiceNumber'), '<input class="form-control" data-h="invoiceNumber" value="' + esc(doc.invoiceNumber) + '">', 3) : roField(l('Inv:InvoiceNumber'), esc(doc.invoiceNumber));
            h += ed ? field(l('Inv:InvoiceDate'), '<input type="date" class="form-control" data-h="invoiceDate" value="' + esc(doc.invoiceDate) + '">', 3) : roField(l('Inv:InvoiceDate'), inv.dateOnly(doc.invoiceDate));
        } else {
            h += fromEditable ? field(l(t === T.Transfer ? 'Inv:WarehouseFrom' : 'Inv:Warehouse'), sel('warehouseFromId', refs.warehouses, whFn, doc.warehouseFromId))
                : roField(l(t === T.Transfer ? 'Inv:WarehouseFrom' : 'Inv:Warehouse'), esc(doc.warehouseFromName || whName(doc.warehouseFromId)));
            if (t === T.Transfer) {
                h += ed ? field(l('Inv:WarehouseTo'), sel('warehouseToId', refs.warehouses, whFn, doc.warehouseToId)) : roField(l('Inv:WarehouseTo'), esc(doc.warehouseToName));
            }
            if (t === T.Writeoff) {
                h += ed ? field(l('Inv:Reason'), sel('reasonId', refs.reasons, function (r) { return r.name; }, doc.reasonId)) : roField(l('Inv:Reason'), esc(doc.reasonName));
            }
            if (t === T.Return) {
                h += ed ? field(l('Inv:Supplier'), sel('supplierId', refs.suppliers, function (s) { return s.name; }, doc.supplierId)) : roField(l('Inv:Supplier'), esc(doc.supplierName));
            }
            if (t === T.Inventory && doc.snapshotAt) { h += roField(l('Inv:Created'), inv.date(doc.snapshotAt)); }
        }
        h += ed ? field(l('Inv:Comment'), '<textarea class="form-control" rows="1" data-h="comment">' + esc(doc.comment) + '</textarea>', 12)
            : (doc.comment ? field(l('Inv:Comment'), '<div class="form-control-plaintext" style="white-space:pre-line">' + esc(doc.comment) + '</div>', 12) : '');
        $('#DocHeader').html(h);
    }

    function readHeader() {
        var h = {};
        $('#DocHeader [data-h]').each(function () { var v = $(this).val(); h[$(this).data('h')] = v === '' ? null : v; });
        return h;
    }

    // ================= Строки =================

    function itemSelect(value) {
        return '<select class="form-select form-select-sm" data-l="itemId"><option value="">' + esc(l('Inv:Item')) + '…</option>' +
            refs.items.map(function (i) { return '<option value="' + i.id + '"' + (i.id === value ? ' selected' : '') + '>' + esc(i.name + ' · ' + i.sku) + '</option>'; }).join('') + '</select>';
    }
    function unitSelect(it, value) {
        if (!it) { return '<select class="form-select form-select-sm" data-l="unitId" disabled></select>'; }
        var h = '<option value="">' + esc(inv.unitText(it.baseUnit)) + '</option>';
        it.units.forEach(function (u) { h += '<option value="' + u.id + '"' + (u.id === value ? ' selected' : '') + '>' + esc(u.unitName) + '</option>'; });
        return '<select class="form-select form-select-sm" data-l="unitId">' + h + '</select>';
    }

    function renderLines() {
        var t = doc.type, ed = editable();
        var $th = $('#LinesTable thead'), $tb = $('#LinesTable tbody').empty(), $tf = $('#LinesTable tfoot').empty();
        $('#LinesActions').empty();

        if (t === T.Inventory && !isNew()) { return renderInventoryLines(ed && doc.status === S.Draft); }
        if (t === T.Transfer && doc.status === S.InTransit && perms.transferReceive) { return renderReceiveLines(); }

        if (ed) {
            if (t === T.Receipt) {
                $th.html('<tr><th style="min-width:260px">' + esc(l('Inv:Item')) + '</th><th style="width:100px">' + esc(l('Inv:Qty')) + '</th><th style="width:140px">' + esc(l('Inv:Unit')) +
                    '</th><th style="width:120px">' + esc(l('Inv:UnitCost')) + '</th><th style="min-width:120px">' + esc(l('Inv:BatchNumber')) + '</th><th style="min-width:120px">' + esc(l('Inv:Serial')) + '</th><th style="width:150px">' +
                    esc(l('Inv:ExpiresAt')) + '</th><th class="text-end">' + esc(l('Inv:Amount')) + '</th><th></th></tr>');
            } else {
                $th.html('<tr><th style="min-width:260px">' + esc(l('Inv:Item')) + '</th><th style="min-width:200px">' + esc(l('Inv:Batch')) + '</th><th style="width:100px">' + esc(l('Inv:Qty')) +
                    '</th><th style="width:140px">' + esc(l('Inv:Unit')) + '</th><th></th></tr>');
            }
            lines.forEach(function (ln, i) {
                var it = item(ln.itemId);
                var row = '<tr data-i="' + i + '"><td>' + itemSelect(ln.itemId) + '</td>';
                if (t === T.Receipt) {
                    var sum = ln.price && ln.qty ? Math.round(ln.price * 100 * ln.qty) : null;
                    row += '<td><input type="number" step="any" min="0" class="form-control form-control-sm" data-l="qty" value="' + (ln.qty ?? '') + '"></td>' +
                        '<td>' + unitSelect(it, ln.unitId) + '</td>' +
                        '<td><input type="number" step="0.01" min="0" class="form-control form-control-sm" data-l="price" value="' + (ln.price ?? '') + '"></td>' +
                        '<td><input class="form-control form-control-sm" data-l="batchNumber" value="' + esc(ln.batchNumber) + '"' + (it && !it.trackBatches ? ' disabled' : '') + '></td>' +
                        '<td><input class="form-control form-control-sm" data-l="serialNumber" value="' + esc(ln.serialNumber) + '"' + (it && !it.trackSerials ? ' disabled' : '') + '></td>' +
                        '<td><input type="date" class="form-control form-control-sm" data-l="expiresAt" value="' + esc(ln.expiresAt) + '"' + (it && !it.trackExpiry ? ' disabled' : '') + '></td>' +
                        '<td class="text-end text-nowrap" data-sum>' + inv.money(sum) + '</td>';
                } else {
                    row += '<td><select class="form-select form-select-sm" data-l="batchId"><option value="">' + esc(l('Inv:AutoBatch')) + '</option>' +
                        (ln.batches || []).map(function (b) {
                            var txt = (b.batchNumber || b.serialNumber || '—') + (b.expiresAt ? ' · ' + inv.dateOnly(b.expiresAt) : '') + ' · ' + inv.qty(b.qty);
                            return '<option value="' + b.batchId + '"' + (b.batchId === ln.batchId ? ' selected' : '') + '>' + esc(txt) + '</option>';
                        }).join('') + '</select></td>' +
                        '<td><input type="number" step="any" min="0" class="form-control form-control-sm" data-l="qty" value="' + (ln.qty ?? '') + '"></td>' +
                        '<td>' + unitSelect(it, ln.unitId) + '</td>';
                }
                row += '<td class="text-end"><button class="btn btn-sm btn-outline-danger" data-remove="' + i + '"><i class="fa fa-times"></i></button></td></tr>';
                $tb.append(row);
            });
            $('#LinesActions').html('<button class="btn btn-sm btn-outline-primary" id="AddLine"><i class="fa fa-plus me-1"></i>' + esc(l('Inv:AddLine')) + '</button>');
            if (t !== T.Receipt) { lines.forEach(function (ln, i) { if (ln.itemId && !ln.batches) { loadBatches(i); } }); }
            return;
        }

        // Только чтение
        var showActual = t === T.Transfer && doc.status === S.Received;
        $th.html('<tr><th>' + esc(l('Inv:Item')) + '</th><th>' + esc(l('Inv:Batch')) + '</th><th>' + esc(l('Inv:ExpiresAt')) + '</th><th class="text-end">' + esc(l('Inv:Qty')) + '</th>' +
            (showActual ? '<th class="text-end">' + esc(l('Inv:Actual')) + '</th>' : '') +
            '<th class="text-end">' + esc(l('Inv:Cost')) + '</th><th class="text-end">' + esc(l('Inv:Amount')) + '</th></tr>');
        lines.forEach(function (ln) {
            var q = inv.qty(ln.sentQty) + ' ' + esc(inv.unitText(ln.baseUnit)) + (ln.unitName ? ' <span class="text-muted small">(' + inv.qty(ln.qty) + ' ' + esc(ln.unitName) + ')</span>' : '');
            $tb.append('<tr><td><a href="' + abp.appPath + 'Inventory/Items/Card?id=' + ln.itemId + '">' + esc(ln.itemName) + '</a> <span class="text-muted small">' + esc(ln.itemSku) + '</span></td>' +
                '<td>' + esc(ln.batchNumber || '') + (ln.serialNumber ? ' <span class="text-muted">SN ' + esc(ln.serialNumber) + '</span>' : '') + '</td>' +
                '<td>' + inv.dateOnly(ln.expiresAt) + '</td><td class="text-end">' + q + '</td>' +
                (showActual ? '<td class="text-end">' + inv.qty(ln.actualQty) + '</td>' : '') +
                '<td class="text-end">' + inv.money(ln.unitCost) + '</td><td class="text-end">' + inv.money(ln.totalCost) + '</td></tr>');
        });
        if (!lines.length) { $tb.append('<tr><td colspan="7" class="text-muted text-center">' + esc(l('Inv:NoLines')) + '</td></tr>'); }
        $tf.html('<tr><th colspan="' + (showActual ? 6 : 5) + '" class="text-end">' + esc(l('Inv:Total')) + '</th><th class="text-end">' + inv.money(doc.totalCost) + '</th></tr>');
    }

    function renderInventoryLines(canEnter) {
        $('#LinesTable thead').html('<tr><th>' + esc(l('Inv:Item')) + '</th><th>' + esc(l('Inv:Batch')) + '</th><th>' + esc(l('Inv:ExpiresAt')) + '</th><th class="text-end">' +
            esc(l('Inv:Expected')) + '</th><th class="text-end" style="width:140px">' + esc(l('Inv:Actual')) + '</th><th class="text-end">' + esc(l('Inv:Diff')) + '</th><th class="text-end">' + esc(l('Inv:Amount')) + '</th></tr>');
        var $tb = $('#LinesTable tbody');
        lines.forEach(function (ln, i) {
            var actual = ln.actualQty;
            var diff = actual === null || actual === undefined ? null : actual - (ln.expectedQty || 0);
            var cell = canEnter ? '<input type="number" step="any" min="0" class="form-control form-control-sm text-end" data-actual="' + i + '" placeholder="' + inv.qty(ln.expectedQty) + '" value="' + (actual ?? '') + '">'
                : inv.qty(actual);
            $tb.append('<tr><td>' + esc(ln.itemName) + ' <span class="text-muted small">' + esc(ln.itemSku) + '</span></td><td>' + esc(ln.batchNumber || '') + '</td><td>' + inv.dateOnly(ln.expiresAt) +
                '</td><td class="text-end">' + inv.qty(ln.expectedQty) + ' ' + esc(inv.unitText(ln.baseUnit)) + '</td><td class="text-end">' + cell + '</td><td class="text-end ' +
                (diff < 0 ? 'text-danger' : diff > 0 ? 'text-success' : '') + '" data-diff="' + i + '">' + (diff === null ? '' : inv.qty(diff)) + '</td><td class="text-end">' +
                (doc.status === S.Posted ? inv.money(ln.totalCost) : '') + '</td></tr>');
        });
        if (!lines.length) { $tb.append('<tr><td colspan="7" class="text-muted text-center">' + esc(l('Inv:NoLines')) + '</td></tr>'); }
        if (doc.status === S.Posted) { $('#LinesTable tfoot').html('<tr><th colspan="6" class="text-end">' + esc(l('Inv:Total')) + '</th><th class="text-end">' + inv.money(doc.totalCost) + '</th></tr>'); }
    }

    function renderReceiveLines() {
        $('#LinesTable thead').html('<tr><th>' + esc(l('Inv:Item')) + '</th><th>' + esc(l('Inv:Batch')) + '</th><th>' + esc(l('Inv:ExpiresAt')) + '</th><th class="text-end">' +
            esc(l('Inv:Sent')) + '</th><th class="text-end" style="width:140px">' + esc(l('Inv:Actual')) + '</th></tr>');
        var $tb = $('#LinesTable tbody');
        lines.forEach(function (ln, i) {
            $tb.append('<tr><td>' + esc(ln.itemName) + '</td><td>' + esc(ln.batchNumber || '') + '</td><td>' + inv.dateOnly(ln.expiresAt) + '</td><td class="text-end">' +
                inv.qty(ln.sentQty) + ' ' + esc(inv.unitText(ln.baseUnit)) + '</td><td><input type="number" step="any" min="0" max="' + ln.sentQty +
                '" class="form-control form-control-sm text-end" data-received="' + i + '" value="' + ln.sentQty + '"></td></tr>');
        });
    }

    function loadBatches(i) {
        var ln = lines[i];
        var wh = readHeader().warehouseFromId || doc.warehouseFromId;
        if (!ln.itemId || !wh) { return; }
        inv.stock.getAvailableBatches(wh, ln.itemId).then(function (res) {
            ln.batches = res.items.filter(function (b) { return b.batchId; });
            renderLinesKeepHeader();
        });
    }

    function renderLinesKeepHeader() { readLines(); renderLines(); }

    function readLines() {
        if (!editable()) { return; }
        $('#LinesTable tbody tr[data-i]').each(function () {
            var ln = lines[Number($(this).data('i'))];
            if (!ln) { return; }
            $(this).find('[data-l]').each(function () {
                var k = $(this).data('l'), v = $(this).val();
                if (k === 'qty' || k === 'price') { ln[k] = inv.num(v); } else { ln[k] = v === '' ? null : v; }
            });
        });
    }

    // ================= Действия =================

    function renderActions() {
        var $a = $('#DocActions').empty(), t = doc.type, st = doc.status;
        if (editable()) {
            $a.append('<button class="btn btn-outline-primary" id="SaveDoc"><i class="fa fa-save me-1"></i>' + esc(l('Save')) + '</button>');
        }
        if (!isNew() && st === S.Draft && canPostType(t) && t !== T.Visit) {
            var label = t === T.Transfer ? 'Inv:Send' : t === T.Inventory ? 'Inv:Approve' : 'Inv:Post';
            $a.append('<button class="btn btn-success" id="PostDoc"><i class="fa fa-check me-1"></i>' + esc(l(label)) + '</button>');
        }
        if (!isNew() && t === T.Transfer && st === S.InTransit && perms.transferReceive) {
            $a.append('<button class="btn btn-success" id="ReceiveDoc"><i class="fa fa-box-open me-1"></i>' + esc(l('Inv:Receive')) + '</button>');
        }
        var cancellable = st === S.Draft || st === S.Pending || (st === S.Posted && (t === T.Receipt || t === T.Writeoff || t === T.Return));
        if (!isNew() && cancellable && canEditType(t)) {
            $a.append('<button class="btn btn-outline-danger" id="CancelDoc"><i class="fa fa-ban me-1"></i>' + esc(l(st === S.Posted ? 'Inv:Storno' : 'Inv:CancelDoc')) + '</button>');
        }
    }

    function render() {
        $('#DocTitle').text(inv.enumText('StockDocumentType', doc.type) + (doc.number ? ' ' + doc.number : ''));
        $('#DocStatus').html(isNew() ? '' : inv.statusBadge(doc.status));
        var hint = '';
        if (doc.status === S.Pending) { hint = l('Inv:PendingHint'); }
        else if (doc.type === T.Inventory && doc.status === S.Draft && !isNew()) { hint = l('Inv:InventoryHint'); }
        else if (doc.type === T.Transfer && doc.status === S.InTransit) { hint = l('Inv:ReceiveHint'); }
        $('#DocHint').html(hint ? '<div class="alert alert-info py-2">' + esc(hint) + '</div>' : '');
        renderHeader();
        renderLines();
        renderActions();
        var meta = [];
        if (doc.creationTime) { meta.push(l('Inv:Created') + ': ' + inv.date(doc.creationTime) + (doc.creatorName ? ' · ' + doc.creatorName : '')); }
        if (doc.postedAt) { meta.push(l('Inv:PostedAt') + ': ' + inv.date(doc.postedAt) + (doc.postedByName ? ' · ' + doc.postedByName : '')); }
        if (doc.receivedAt) { meta.push(l('Inv:ReceivedAt') + ': ' + inv.date(doc.receivedAt)); }
        if (doc.sourceDocumentId) { meta.push('<a href="' + abp.appPath + 'Inventory/Documents/Edit?id=' + doc.sourceDocumentId + '">' + esc(l('Inv:Source')) + '</a>'); }
        $('#DocMeta').html(meta.map(function (m) { return '<span class="me-3">' + m + '</span>'; }).join(''));
    }

    function lineInputs() {
        readLines();
        return lines.filter(function (ln) { return ln.itemId; }).map(function (ln) {
            return {
                itemId: ln.itemId, qty: ln.qty || 0, unitId: ln.unitId || null, batchId: doc.type === T.Receipt ? null : (ln.batchId || null),
                unitCost: doc.type === T.Receipt ? inv.toTiyn(ln.price) : null,
                batchNumber: ln.batchNumber || null, serialNumber: ln.serialNumber || null, expiresAt: ln.expiresAt || null
            };
        });
    }

    function inventoryInputs() {
        var counts = [];
        $('[data-actual]').each(function () {
            var ln = lines[Number($(this).data('actual'))];
            var v = $(this).val();
            ln.actualQty = v === '' ? null : inv.num(v);
            if (ln.actualQty !== null) { counts.push({ lineId: ln.id, actualQty: ln.actualQty, itemId: ln.itemId, batchId: ln.batchId }); }
        });
        return counts;
    }

    function save() {
        var h = readHeader();
        if (doc.type === T.Inventory && !isNew()) {
            var counts = inventoryInputs();
            return inv.stock.update(doc.id, {
                comment: h.comment || '', concurrencyStamp: doc.concurrencyStamp,
                lines: counts.map(function (c) { return { itemId: c.itemId, batchId: c.batchId, qty: c.actualQty, actualQty: c.actualQty }; })
            });
        }
        var payload = {
            warehouseFromId: h.warehouseFromId || null, warehouseToId: h.warehouseToId || null, supplierId: h.supplierId || null,
            invoiceNumber: h.invoiceNumber === undefined ? null : (h.invoiceNumber || ''), invoiceDate: h.invoiceDate || null, reasonId: h.reasonId || null,
            comment: h.comment === undefined ? null : (h.comment || ''), lines: doc.type === T.Inventory ? null : lineInputs()
        };
        if (isNew()) {
            payload.type = doc.type;
            return inv.stock.create(payload);
        }
        payload.concurrencyStamp = doc.concurrencyStamp;
        return inv.stock.update(doc.id, payload);
    }

    $(document).on('click', '#SaveDoc', function () {
        var wasNew = isNew();
        save().then(function (d) {
            abp.notify.success(l('Inv:Saved'));
            if (wasNew) { window.location.href = abp.appPath + 'Inventory/Documents/Edit?id=' + d.id; } else { loadDoc(d.id); }
        });
    });

    $(document).on('click', '#PostDoc', function () {
        abp.message.confirm(l('Inv:PostConfirm', doc.number)).then(function (ok) {
            if (!ok) { return; }
            var chain = editable() ? save() : Promise.resolve(doc);
            chain.then(function (d) {
                var counts = doc.type === T.Inventory ? inventoryInputs().map(function (c) { return { lineId: c.lineId, actualQty: c.actualQty }; }) : null;
                return inv.stock.post(d.id, { concurrencyStamp: d.concurrencyStamp, counts: counts });
            }).then(function (d) {
                abp.notify.success(d.status === S.Pending ? l('Inv:PendingHint') : l('Inv:Posted'));
                loadDoc(d.id);
            }).catch(function () { loadDoc(doc.id); });
        });
    });

    $(document).on('click', '#ReceiveDoc', function () {
        var input = [];
        $('[data-received]').each(function () {
            var ln = lines[Number($(this).data('received'))];
            input.push({ lineId: ln.id, actualQty: inv.num($(this).val()) || 0 });
        });
        inv.stock.receive(doc.id, { lines: input, concurrencyStamp: doc.concurrencyStamp }).then(function (d) {
            abp.notify.success(l('Inv:Posted'));
            loadDoc(d.id);
        });
    });

    $(document).on('click', '#CancelDoc', function () {
        inv.formModal({
            title: l('Inv:CancelConfirm', doc.number),
            okText: l('Inv:CancelDoc'),
            fields: [{ name: 'comment', label: l('Inv:CancelReason'), type: 'textarea', required: doc.status === S.Posted }],
            submit: function (v) { return inv.stock.cancel(doc.id, { comment: v.comment, concurrencyStamp: doc.concurrencyStamp }); }
        }).then(function (d) { loadDoc(d.id); });
    });

    $(document).on('click', '#AddLine', function () { readLines(); lines.push(newLine()); renderLines(); });
    $(document).on('click', '[data-remove]', function () { readLines(); lines.splice(Number($(this).data('remove')), 1); renderLines(); });

    $(document).on('change', '#LinesTable [data-l="itemId"]', function () {
        readLines();
        var i = Number($(this).closest('tr').data('i'));
        lines[i].unitId = null; lines[i].batchId = null; lines[i].batches = null;
        var it = item(lines[i].itemId);
        if (it && doc.type === T.Receipt && it.units.length) { lines[i].unitId = it.units[0].id; }
        renderLines();
    });
    // Пересчёт суммы строки без перерисовки таблицы (иначе теряется фокус ввода).
    $(document).on('input change', '#LinesTable [data-l="qty"], #LinesTable [data-l="price"]', function () {
        if (doc.type !== T.Receipt) { return; }
        var $tr = $(this).closest('tr');
        var q = inv.num($tr.find('[data-l="qty"]').val()), p = inv.num($tr.find('[data-l="price"]').val());
        $tr.find('[data-sum]').text(q && p ? inv.money(Math.round(p * 100 * q)) : '');
    });
    $(document).on('change', '#DocHeader [data-h="warehouseFromId"]', function () {
        if (isOut(doc.type)) { readLines(); lines.forEach(function (ln) { ln.batches = null; ln.batchId = null; }); renderLines(); }
    });
    $(document).on('input', '[data-actual]', function () {
        var i = Number($(this).data('actual')), ln = lines[i], v = $(this).val();
        var diff = v === '' ? null : inv.num(v) - (ln.expectedQty || 0);
        $('[data-diff="' + i + '"]').text(diff === null ? '' : inv.qty(diff)).toggleClass('text-danger', diff < 0).toggleClass('text-success', diff > 0);
    });
});
