/* Общие помощники раздела «Склад»: форматирование, справочники, модальные формы. */
(function () {
    var l = abp.localization.getResource('Dental');
    var catalog = dental.inventory.inventoryCatalog;
    var stock = dental.inventory.stock;

    function esc(s) { return $('<div>').text(s === null || s === undefined ? '' : String(s)).html(); }

    var moneyFmt = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 0, maximumFractionDigits: 2 });
    var qtyFmt = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 4 });

    function enumText(type, value) {
        return value === null || value === undefined ? '' : l('Enum:' + type + '.' + value);
    }

    /** Тиыны → «1 234,5 ₸». */
    function money(tiyn) { return tiyn === null || tiyn === undefined ? '' : moneyFmt.format(tiyn / 100) + ' ₸'; }
    function qty(q) { return q === null || q === undefined ? '' : qtyFmt.format(q); }
    function date(d) { return d ? luxonDate(d) : ''; }
    function luxonDate(d) {
        var s = String(d);
        if (/^\d{4}-\d{2}-\d{2}$/.test(s)) { var p = s.split('-'); return p[2] + '.' + p[1] + '.' + p[0]; }
        var dt = new Date(s.endsWith('Z') || s.indexOf('+') > 10 ? s : s + 'Z');
        return dt.toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    }
    function dateOnly(d) { return d ? luxonDate(String(d).substring(0, 10)) : ''; }

    var statusClass = { 0: 'secondary', 1: 'warning text-dark', 2: 'success', 3: 'info text-dark', 4: 'primary', 5: 'dark' };
    function statusBadge(s) { return '<span class="badge bg-' + statusClass[s] + '">' + esc(enumText('StockDocumentStatus', s)) + '</span>'; }

    function options(list, valueKey, textFn, emptyText, selected) {
        var html = emptyText !== null && emptyText !== undefined ? '<option value="">' + esc(emptyText) + '</option>' : '';
        (list || []).forEach(function (x) {
            var v = x[valueKey];
            html += '<option value="' + esc(v) + '"' + (String(v) === String(selected) ? ' selected' : '') + '>' + esc(textFn(x)) + '</option>';
        });
        return html;
    }

    /** Категории деревом: [{id, name, depth}] в порядке обхода. */
    function categoryTree(categories) {
        var byParent = {};
        categories.forEach(function (c) { var k = c.parentId || ''; (byParent[k] = byParent[k] || []).push(c); });
        var result = [];
        (function walk(parent, depth) {
            (byParent[parent] || []).sort(function (a, b) { return a.name.localeCompare(b.name); }).forEach(function (c) {
                result.push($.extend({ depth: depth }, c));
                walk(c.id, depth + 1);
            });
        })('', 0);
        return result;
    }

    function categoryOptions(categories, emptyText, selected) {
        return options(categoryTree(categories), 'id', function (c) { return '  '.repeat(c.depth) + c.name; }, emptyText, selected);
    }

    /**
     * Модальная форма. fields: [{name, label, type: text|number|select|checkbox|textarea|date, options: html, required, value, step, col}]
     * extraHtml — дополнительная разметка; onRender($m); collect($m, values) — дополнить значения. Возвращает Promise<values>.
     */
    function formModal(opts) {
        return new Promise(function (resolve) {
            var body = '<div class="row">';
            (opts.fields || []).forEach(function (f) {
                var id = 'invf_' + f.name;
                var col = f.col || 12;
                var v = f.value === null || f.value === undefined ? '' : f.value;
                body += '<div class="col-md-' + col + ' mb-3">';
                if (f.type === 'checkbox') {
                    body += '<div class="form-check mt-2"><input type="checkbox" class="form-check-input" id="' + id + '" name="' + f.name + '"' + (f.value ? ' checked' : '') + '>' +
                        '<label class="form-check-label" for="' + id + '">' + esc(f.label) + '</label></div>';
                } else {
                    body += '<label class="form-label" for="' + id + '">' + esc(f.label) + (f.required ? ' *' : '') + '</label>';
                    if (f.type === 'select') {
                        body += '<select class="form-select" id="' + id + '" name="' + f.name + '">' + f.options + '</select>';
                    } else if (f.type === 'textarea') {
                        body += '<textarea class="form-control" rows="3" id="' + id + '" name="' + f.name + '">' + esc(v) + '</textarea>';
                    } else {
                        body += '<input class="form-control" id="' + id + '" name="' + f.name + '" type="' + (f.type || 'text') + '"' +
                            (f.step ? ' step="' + f.step + '"' : '') + ' value="' + esc(v) + '"' + (f.required ? ' required' : '') + '>';
                    }
                }
                body += '</div>';
            });
            body += '</div>' + (opts.extraHtml || '');
            var $m = $('<div class="modal fade" tabindex="-1"><div class="modal-dialog ' + (opts.size || '') + '"><div class="modal-content">' +
                '<div class="modal-header"><h5 class="modal-title">' + esc(opts.title) + '</h5><button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>' +
                '<form><div class="modal-body">' + body + '</div><div class="modal-footer">' +
                '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">' + esc(l('Cancel')) + '</button>' +
                '<button type="submit" class="btn btn-primary">' + esc(opts.okText || l('Save')) + '</button></div></form></div></div></div>');
            $('body').append($m);
            (opts.fields || []).forEach(function (f) { if (f.type === 'select' && f.value !== undefined && f.value !== null) { $m.find('[name="' + f.name + '"]').val(String(f.value)); } });
            if (opts.onRender) { opts.onRender($m); }
            var modal = new bootstrap.Modal($m[0]);
            $m.on('hidden.bs.modal', function () { $m.remove(); });
            $m.find('form').on('submit', function (e) {
                e.preventDefault();
                var values = {};
                (opts.fields || []).forEach(function (f) {
                    var $i = $m.find('[name="' + f.name + '"]');
                    var v = f.type === 'checkbox' ? $i.is(':checked') : $i.val();
                    if (f.type === 'number') { v = v === '' ? null : Number(v); }
                    if ((f.type === 'select' || f.type === 'date' || f.type === 'text' || f.type === 'textarea' || !f.type) && v === '') { v = null; }
                    values[f.name] = v;
                });
                if (opts.collect) { opts.collect($m, values); }
                var r = opts.submit ? opts.submit(values) : null;
                if (r && r.then) {
                    r.then(function (res) { modal.hide(); resolve(res === undefined ? values : res); });
                } else { modal.hide(); resolve(values); }
            });
            modal.show();
        });
    }

    var cache = {};
    function once(key, fn) { return cache[key] || (cache[key] = fn()); }

    window.dentalInv = {
        l: l, catalog: catalog, stock: stock, esc: esc, enumText: enumText, money: money, qty: qty, date: date, dateOnly: dateOnly,
        statusBadge: statusBadge, options: options, categoryTree: categoryTree, categoryOptions: categoryOptions, formModal: formModal,
        unitText: function (u) { return enumText('BaseUnit', u); },
        warehouses: function () { return once('wh', function () { return catalog.getWarehouses({}).then(function (r) { return r.items; }); }); },
        categories: function () { return once('cat', function () { return catalog.getCategories().then(function (r) { return r.items; }); }); },
        reasons: function () { return once('rs', function () { return catalog.getWriteoffReasons().then(function (r) { return r.items; }); }); },
        suppliers: function () { return once('sp', function () { return catalog.getSupplierLookup().then(function (r) { return r.items; }); }); },
        items: function () { return once('it', function () { return catalog.getItemLookup(null, 200).then(function (r) { return r.items; }); }); },
        /** Тенге (строка/число) → тиыны. */
        toTiyn: function (v) { return v === null || v === '' || v === undefined ? null : Math.round(Number(String(v).replace(',', '.')) * 100); },
        num: function (v) { return v === null || v === '' || v === undefined ? null : Number(String(v).replace(',', '.')); }
    };
})();
