$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.catalog.catalog;
    var esc = dentalUi.esc;
    var canPrices = abp.auth.isGranted('Dental.Catalog.PricesManage');
    var canTechCards = abp.auth.isGranted('Dental.Catalog.TechCardsManage');
    $('[data-perm=prices]').toggleClass('d-none', !canPrices);
    $('[data-perm=techcards]').toggleClass('d-none', !canTechCards);

    var categories = [], selectedCategory = null, services = [], priceLists = [];
    var modal = function (id) { return bootstrap.Modal.getOrCreateInstance(document.getElementById(id)); };
    var tenge = function (tiyn) { return tiyn === null || tiyn === undefined ? '' : (tiyn / 100); };
    var toTiyn = function (v) { return Math.round(parseFloat(String(v).replace(',', '.').replace(/\s/g, '')) * 100); };

    dental.branches.branch.getLookup().then(function (r) {
        r.items.forEach(function (b) {
            $('#PriceBranch').append($('<option>').val(b.id).text(b.name));
            $('#PriceListModal [name=branchId]').append($('<option>').val(b.id).text(b.name));
        });
    });

    // ---------- Категории (дерево) ----------
    function childrenOf(parentId) {
        return categories.filter(function (c) { return (c.parentId || null) === parentId; });
    }
    function walk(parentId, depth, fn) {
        childrenOf(parentId).forEach(function (c) { fn(c, depth); walk(c.id, depth + 1, fn); });
    }
    function categoryOptions($select, emptyText, exceptId) {
        $select.empty();
        if (emptyText !== null) { $select.append($('<option>').val('').text(emptyText)); }
        walk(null, 0, function (c, d) {
            if (c.id === exceptId) { return; }
            $select.append($('<option>').val(c.id).text('  '.repeat(d) + c.name));
        });
    }
    function renderTree() {
        var html = '<a href="#" class="list-group-item list-group-item-action' + (selectedCategory ? '' : ' active') + '" data-id="">' + esc(l('Catalog:AllCategories')) + '</a>';
        walk(null, 0, function (c, d) {
            html += '<a href="#" class="list-group-item list-group-item-action d-flex justify-content-between' + (selectedCategory === c.id ? ' active' : '') + '" data-id="' + c.id + '" style="padding-left:' + (12 + d * 16) + 'px">' +
                '<span>' + esc(c.name) + '</span>' + (canPrices ? '<i class="fa fa-pen small opacity-50" data-edit="' + c.id + '"></i>' : '') + '</a>';
        });
        $('#CategoryTree').html(html);
    }
    function loadCategories() {
        return service.getCategories().then(function (r) { categories = r.items; renderTree(); });
    }
    $('#CategoryTree').on('click', 'a', function (e) {
        e.preventDefault();
        var edit = $(e.target).data('edit');
        if (edit) { openCategory(categories.find(function (c) { return c.id === edit; })); return; }
        selectedCategory = $(this).data('id') || null;
        renderTree();
        loadServices();
    });

    var $cat = $('#CategoryModal');
    function openCategory(c) {
        $cat.find('[data-role=title]').text(c ? l('Catalog:EditCategory') : l('Catalog:NewCategory'));
        $cat.find('[name=id]').val(c ? c.id : '');
        $cat.find('[name=name]').val(c ? c.name : '');
        $cat.find('[name=sort]').val(c ? c.sort : 0);
        categoryOptions($cat.find('[name=parentId]'), l('Catalog:NoParent'), c ? c.id : null);
        $cat.find('[name=parentId]').val(c ? (c.parentId || '') : (selectedCategory || ''));
        $cat.find('[data-role=delete]').toggleClass('d-none', !c);
        modal('CategoryModal').show();
    }
    $('#NewCategoryButton').on('click', function () { openCategory(null); });
    $cat.find('form').on('submit', function (e) {
        e.preventDefault();
        var id = $cat.find('[name=id]').val();
        var input = { name: $cat.find('[name=name]').val().trim(), parentId: $cat.find('[name=parentId]').val() || null, sort: parseInt($cat.find('[name=sort]').val(), 10) || 0 };
        if (!input.name) { return; }
        (id ? service.updateCategory(id, input) : service.createCategory(input)).then(function () {
            modal('CategoryModal').hide();
            abp.notify.success(l('SavedSuccessfully'));
            loadCategories();
        });
    });
    $cat.find('[data-role=delete]').on('click', function () {
        var id = $cat.find('[name=id]').val();
        abp.message.confirm(l('Catalog:CategoryDeleteConfirm', $cat.find('[name=name]').val())).then(function (ok) {
            if (!ok) { return; }
            service.deleteCategory(id).then(function () {
                modal('CategoryModal').hide();
                if (selectedCategory === id) { selectedCategory = null; }
                loadCategories().then(loadServices);
            });
        });
    });

    // ---------- Услуги ----------
    function loadServices() {
        return service.getServices({
            categoryId: selectedCategory,
            filter: $('#ServiceFilter').val() || null,
            includeInactive: $('#ShowInactive').is(':checked'),
            branchId: $('#PriceBranch').val() || null
        }).then(function (r) {
            services = r.items;
            $('#ServicesBody').html(services.length ? services.map(function (s) {
                return '<tr class="' + (s.isActive ? '' : 'text-muted') + '" data-id="' + s.id + '">' +
                    '<td><code>' + esc(s.code) + '</code></td>' +
                    '<td>' + esc(s.name) + (s.isActive ? '' : ' <span class="badge bg-secondary">' + esc(l('Inactive')) + '</span>') + '</td>' +
                    '<td class="text-end">' + s.durationMin + '</td>' +
                    '<td class="text-end">' + (s.price === null || s.price === undefined ? '<span class="small text-muted">' + esc(l('Catalog:NoPrice')) + '</span>' : dentalUi.money(s.price)) + '</td>' +
                    '<td><button type="button" class="btn btn-sm text-nowrap ' + (s.hasTechCard ? 'btn-outline-success' : 'btn-outline-secondary') + '" data-act="techcard">' +
                    '<i class="fa fa-list-check me-1"></i>' + esc(s.hasTechCard ? l('Catalog:TechCard') : l('Catalog:TechCardNone')) + '</button></td>' +
                    '<td>' + (canPrices ? '<button type="button" class="btn btn-sm btn-link" data-act="edit"><i class="fa fa-pen"></i></button>' : '') + '</td></tr>';
            }).join('') : '<tr><td colspan="6" class="text-muted">' + esc(l('NothingFound')) + '</td></tr>');
        });
    }
    $('#ServiceFilter').on('input', dentalUi.debounce(loadServices, 300));
    $('#ShowInactive, #PriceBranch').on('change', loadServices);
    $('#ServicesBody').on('click', 'button[data-act]', function () {
        var s = services.find(function (x) { return x.id === $(this).closest('tr').data('id'); }, this);
        if ($(this).data('act') === 'edit') { openService(s); } else { openTechCard(s); }
    });

    var $svc = $('#ServiceModal');
    function openService(s) {
        $svc.find('[data-role=title]').text(s ? l('Catalog:EditService') : l('Catalog:NewService'));
        categoryOptions($svc.find('[name=categoryId]'), null, null);
        $svc.find('[name=id]').val(s ? s.id : '');
        $svc.find('[name=categoryId]').val(s ? s.categoryId : (selectedCategory || $svc.find('[name=categoryId] option:first').val()));
        $svc.find('[name=code]').val(s ? s.code : '');
        $svc.find('[name=name]').val(s ? s.name : '');
        $svc.find('[name=durationMin]').val(s ? s.durationMin : 30);
        $svc.find('[name=isActive]').prop('checked', s ? s.isActive : true);
        modal('ServiceModal').show();
    }
    $('#NewServiceButton').on('click', function () { openService(null); });
    $svc.find('form').on('submit', function (e) {
        e.preventDefault();
        var id = $svc.find('[name=id]').val();
        var input = {
            categoryId: $svc.find('[name=categoryId]').val(),
            code: $svc.find('[name=code]').val().trim(),
            name: $svc.find('[name=name]').val().trim(),
            durationMin: parseInt($svc.find('[name=durationMin]').val(), 10) || 30,
            isActive: $svc.find('[name=isActive]').is(':checked')
        };
        if (!input.code || !input.name || !input.categoryId) { return; }
        (id ? service.updateService(id, input) : service.createService(input)).then(function () {
            modal('ServiceModal').hide();
            abp.notify.success(l('SavedSuccessfully'));
            loadServices();
        });
    });

    // ---------- Техкарты ----------
    var $tc = $('#TechCardModal'), tcService = null, tcRows = [];
    function renderTcRows() {
        $tc.find('[data-role=rows]').html(tcRows.map(function (r, i) {
            return '<tr><td>' + esc(r.name) + (r.unit ? ' <span class="text-muted small">(' + esc(r.unit) + ')</span>' : '') + '</td>' +
                '<td><input type="number" class="form-control form-control-sm" min="0.0001" step="0.0001" value="' + r.qty + '" data-i="' + i + '" /></td>' +
                '<td><button type="button" class="btn btn-sm btn-link text-danger" data-del="' + i + '"><i class="fa fa-times"></i></button></td></tr>';
        }).join(''));
    }
    function openTechCard(s) {
        tcService = s;
        tcRows = [];
        $tc.find('[data-role=title]').text(l('Catalog:TechCardOf', s.name));
        $tc.find('[data-role=versions]').html('<i class="fa fa-spinner fa-spin"></i>');
        service.getTechCards(s.id).then(function (r) {
            if (!r.items.length) {
                $tc.find('[data-role=versions]').html('<div class="text-muted">' + esc(l('Catalog:TechCardNone')) + '</div>');
            } else {
                $tc.find('[data-role=versions]').html(r.items.map(function (c) {
                    return '<div class="border rounded p-2 mb-2' + (c.isActive ? ' border-success' : '') + '">' +
                        '<div class="small mb-1"><strong>' + esc(l('Catalog:TechCardVersion', c.version)) + '</strong> · ' + dentalUi.date(c.creationTime) +
                        (c.isActive ? ' <span class="badge bg-success">' + esc(l('Catalog:TechCardActive')) + '</span>' : '') + '</div>' +
                        '<ul class="small mb-0">' + c.items.map(function (i) { return '<li>' + esc(i.itemName) + ' — ' + i.quantity + ' ' + esc(i.baseUnit) + '</li>'; }).join('') + '</ul></div>';
                }).join(''));
                var active = r.items.find(function (c) { return c.isActive; });
                if (active) {
                    tcRows = active.items.map(function (i) { return { id: i.itemId, name: i.itemName, unit: i.baseUnit, qty: i.quantity }; });
                }
            }
            renderTcRows();
        });
        if (canTechCards) {
            service.getItemLookup('', { abpHandleError: false }).then(function (r) { $tc.find('[data-role=no-items]').toggleClass('d-none', r.items.length > 0); });
        }
        modal('TechCardModal').show();
    }
    var guidRe = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    var $itemSearch = $tc.find('[data-role=item-search]'), $itemResults = $tc.find('[data-role=item-results]');
    $itemSearch.on('input', dentalUi.debounce(function () {
        var q = $itemSearch.val().trim();
        if (!q) { $itemResults.removeClass('show'); return; }
        service.getItemLookup(q).then(function (r) {
            var html = r.items.map(function (i) {
                return '<a href="#" class="dropdown-item" data-id="' + i.id + '" data-name="' + esc(i.name) + '" data-unit="' + esc(i.baseUnit) + '">' + esc(i.name) + ' <span class="text-muted small">' + esc(i.baseUnit) + '</span></a>';
            }).join('');
            if (!html && guidRe.test(q)) {
                html = '<a href="#" class="dropdown-item" data-id="' + q + '" data-name="' + q + '" data-unit="">' + esc(l('Catalog:ItemId')) + ': ' + esc(q) + '</a>';
            }
            $itemResults.html(html || '<span class="dropdown-item-text text-muted small">' + esc(l('NothingFound')) + '</span>').addClass('show');
        });
    }, 300));
    $itemResults.on('click', 'a', function (e) {
        e.preventDefault();
        var id = $(this).data('id');
        if (!tcRows.some(function (r) { return r.id === id; })) {
            tcRows.push({ id: id, name: $(this).data('name'), unit: $(this).data('unit'), qty: 1 });
        }
        $itemSearch.val('');
        $itemResults.removeClass('show');
        renderTcRows();
    });
    $tc.on('input', 'input[data-i]', function () { tcRows[$(this).data('i')].qty = parseFloat($(this).val()) || 0; });
    $tc.on('click', '[data-del]', function () { tcRows.splice($(this).data('del'), 1); renderTcRows(); });
    $tc.find('form').on('submit', function (e) {
        e.preventDefault();
        service.createTechCardVersion(tcService.id, { items: tcRows.map(function (r) { return { itemId: r.id, quantity: r.qty }; }) }).then(function () {
            modal('TechCardModal').hide();
            abp.notify.success(l('SavedSuccessfully'));
            loadServices();
        });
    });

    // ---------- Прайс-листы ----------
    function loadPriceLists() {
        return service.getPriceLists().then(function (r) {
            priceLists = r.items;
            $('#PriceListsBody').html(priceLists.map(function (p) {
                return '<tr data-id="' + p.id + '" class="' + (p.isActive ? '' : 'text-muted') + '"><td>' + esc(p.name) + '</td>' +
                    '<td>' + esc(p.branchName || l('Catalog:PriceListNetwork')) + '</td>' +
                    '<td>' + dentalUi.date(p.validFrom) + '</td><td class="text-end">' + p.itemsCount + '</td>' +
                    '<td>' + (p.isActive ? '<i class="fa fa-check text-success"></i>' : '<span class="badge bg-secondary">' + esc(l('Inactive')) + '</span>') + '</td>' +
                    '<td class="text-end text-nowrap">' +
                    '<button class="btn btn-sm btn-outline-primary me-1" data-act="prices"><i class="fa fa-tags me-1"></i>' + esc(l('Catalog:EditPrices')) + '</button>' +
                    (canPrices ? '<button class="btn btn-sm btn-outline-secondary me-1" data-act="bulk">%</button>' +
                        '<button class="btn btn-sm btn-link" data-act="edit"><i class="fa fa-pen"></i></button>' +
                        '<button class="btn btn-sm btn-link text-danger" data-act="delete"><i class="fa fa-trash"></i></button>' : '') +
                    '</td></tr>';
            }).join(''));
        });
    }
    $('#PriceListsBody').on('click', 'button[data-act]', function () {
        var id = $(this).closest('tr').data('id');
        var p = priceLists.find(function (x) { return x.id === id; });
        var act = $(this).data('act');
        if (act === 'prices') { openPrices(p); }
        if (act === 'bulk') { openBulk(p); }
        if (act === 'edit') { openPriceList(p); }
        if (act === 'delete') {
            abp.message.confirm(l('Catalog:PriceListDeleteConfirm', p.name)).then(function (ok) {
                if (ok) { service.deletePriceList(id).then(function () { loadPriceLists(); loadServices(); }); }
            });
        }
    });

    var $pl = $('#PriceListModal');
    function openPriceList(p) {
        $pl.find('[data-role=title]').text(p ? l('Catalog:EditPriceList') : l('Catalog:NewPriceList'));
        $pl.find('[name=id]').val(p ? p.id : '');
        $pl.find('[name=name]').val(p ? p.name : '');
        $pl.find('[name=branchId]').val(p ? (p.branchId || '') : '');
        $pl.find('[name=validFrom]').val(p ? String(p.validFrom).substr(0, 10) : new Date().toISOString().substr(0, 10));
        $pl.find('[name=isActive]').prop('checked', p ? p.isActive : true);
        var $copy = $pl.find('[name=copyFromId]');
        $copy.find('option:not(:first)').remove();
        priceLists.forEach(function (x) { $copy.append($('<option>').val(x.id).text(x.name)); });
        $copy.val('');
        $pl.find('[data-role=copy]').toggleClass('d-none', !!p);
        modal('PriceListModal').show();
    }
    $('#NewPriceListButton').on('click', function () { openPriceList(null); });
    $pl.find('form').on('submit', function (e) {
        e.preventDefault();
        var id = $pl.find('[name=id]').val();
        var input = {
            name: $pl.find('[name=name]').val().trim(),
            branchId: $pl.find('[name=branchId]').val() || null,
            validFrom: $pl.find('[name=validFrom]').val(),
            isActive: $pl.find('[name=isActive]').is(':checked'),
            copyFromId: id ? null : ($pl.find('[name=copyFromId]').val() || null)
        };
        if (!input.name || !input.validFrom) { return; }
        (id ? service.updatePriceList(id, input) : service.createPriceList(input)).then(function () {
            modal('PriceListModal').hide();
            abp.notify.success(l('SavedSuccessfully'));
            loadPriceLists();
            loadServices();
        });
    });

    var $pr = $('#PricesModal'), prList = null, prOriginal = {};
    function openPrices(p) {
        prList = p;
        $pr.find('[data-role=title]').text(l('Catalog:PricesOf', p.name));
        $pr.find('[data-role=filter]').val('');
        $pr.find('[data-role=rows]').html('<tr><td colspan="3"><i class="fa fa-spinner fa-spin"></i></td></tr>');
        var all;
        service.getServices({ includeInactive: true }).then(function (r) { all = r; return service.getPriceItems(p.id); }).then(function (items) {
            prOriginal = {};
            items.items.forEach(function (i) { prOriginal[i.serviceId] = i.price; });
            $pr.find('[data-role=rows]').html(all.items.map(function (s) {
                var v = prOriginal[s.id];
                return '<tr data-search="' + esc((s.code + ' ' + s.name).toLowerCase()) + '"><td><code>' + esc(s.code) + '</code></td><td>' + esc(s.name) + '</td>' +
                    '<td><input type="number" min="0" step="1" class="form-control form-control-sm text-end" data-sid="' + s.id + '" value="' + (v === undefined ? '' : tenge(v)) + '"' +
                    (canPrices ? '' : ' disabled') + ' /></td></tr>';
            }).join(''));
        });
        modal('PricesModal').show();
    }
    $pr.find('[data-role=filter]').on('input', function () {
        var q = $(this).val().trim().toLowerCase();
        $pr.find('[data-role=rows] tr').each(function () { $(this).toggle(!q || String($(this).data('search')).indexOf(q) >= 0); });
    });
    $pr.find('form').on('submit', function (e) {
        e.preventDefault();
        var changes = [];
        $pr.find('input[data-sid]').each(function () {
            var sid = $(this).data('sid'), raw = $(this).val().trim();
            var price = raw === '' ? null : toTiyn(raw);
            var old = prOriginal[sid] === undefined ? null : prOriginal[sid];
            if (price !== old && !(price !== null && isNaN(price))) { changes.push({ serviceId: sid, price: price }); }
        });
        if (!changes.length) { modal('PricesModal').hide(); return; }
        service.setPriceItems(prList.id, { items: changes }).then(function () {
            modal('PricesModal').hide();
            abp.notify.success(l('SavedSuccessfully'));
            loadPriceLists();
            loadServices();
        });
    });

    var $bulk = $('#BulkModal'), bulkList = null;
    function openBulk(p) {
        bulkList = p;
        $bulk.find('[data-role=title]').text(l('Catalog:BulkUpdate') + ' — ' + p.name);
        $bulk.find('[name=percent]').val('');
        categoryOptions($bulk.find('[name=categoryId]'), l('Catalog:AllCategories'), null);
        modal('BulkModal').show();
    }
    $bulk.find('form').on('submit', function (e) {
        e.preventDefault();
        var percent = parseFloat($bulk.find('[name=percent]').val());
        if (isNaN(percent)) { return; }
        var round = parseFloat($bulk.find('[name=roundTo]').val());
        service.bulkUpdatePrices(bulkList.id, {
            percent: percent,
            roundTo: round > 0 ? Math.round(round * 100) : null,
            categoryId: $bulk.find('[name=categoryId]').val() || null
        }).then(function (r) {
            modal('BulkModal').hide();
            abp.notify.success(l('Catalog:BulkDone', r.updated));
            loadPriceLists();
            loadServices();
        });
    });

    loadCategories().then(loadServices);
    loadPriceLists();
});
