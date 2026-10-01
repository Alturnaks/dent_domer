/* Общие помощники UI (форматирование денег/дат, перечисления) + шапка: глобальный поиск пациента и уведомления. */
var dentalUi = (function () {
    var l = function (k) {
        var args = Array.prototype.slice.call(arguments);
        var res = abp.localization.getResource('Dental');
        return res.apply(null, args);
    };

    function esc(s) {
        return $('<div>').text(s === null || s === undefined ? '' : String(s)).html();
    }

    /** Тиыны → "12 345 ₸" (с копейками, если есть). */
    function money(tiyn) {
        if (tiyn === null || tiyn === undefined || tiyn === '') { return ''; }
        var v = Number(tiyn) / 100;
        return v.toLocaleString('ru-RU', { minimumFractionDigits: v % 1 ? 2 : 0, maximumFractionDigits: 2 }) + ' ₸';
    }

    /** "2026-03-01" или ISO → "01.03.2026". */
    function date(v) {
        if (!v) { return ''; }
        var s = String(v);
        var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(s);
        if (m && s.length <= 10) { return m[3] + '.' + m[2] + '.' + m[1]; }
        var d = new Date(s);
        return isNaN(d) ? s : d.toLocaleDateString('ru-RU');
    }

    function dateTime(v) {
        if (!v) { return ''; }
        var d = new Date(v);
        return isNaN(d) ? String(v) : d.toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    }

    /** Значение enum (число или строка) → локализованный текст по ключу Enum:{Type}.{Name}. */
    function enumText(type, value, names) {
        var name = typeof value === 'number' ? (names[value] || value) : value;
        return l('Enum:' + type + '.' + name);
    }

    function enumName(value, names) {
        return typeof value === 'number' ? names[value] : value;
    }

    function age(birthDate) {
        if (!birthDate) { return ''; }
        var b = new Date(birthDate), n = new Date();
        var a = n.getFullYear() - b.getFullYear();
        if (n.getMonth() < b.getMonth() || (n.getMonth() === b.getMonth() && n.getDate() < b.getDate())) { a--; }
        return a;
    }

    function phone(p) {
        if (!p) { return ''; }
        var d = String(p);
        if (d.length === 11) { return '+' + d[0] + ' ' + d.substr(1, 3) + ' ' + d.substr(4, 3) + '-' + d.substr(7, 2) + '-' + d.substr(9, 2); }
        return d;
    }

    function balance(tiyn) {
        if (!tiyn) { return '<span class="text-muted">0 ₸</span>'; }
        return '<span class="' + (tiyn < 0 ? 'text-danger fw-semibold' : 'text-success') + '">' + money(tiyn) + '</span>';
    }

    function debounce(fn, ms) {
        var t;
        return function () {
            var args = arguments, self = this;
            clearTimeout(t);
            t = setTimeout(function () { fn.apply(self, args); }, ms);
        };
    }

    return { l: l, esc: esc, money: money, date: date, dateTime: dateTime, enumText: enumText, enumName: enumName, age: age, phone: phone, balance: balance, debounce: debounce };
})();

$(function () {
    if (!abp.currentUser || !abp.currentUser.isAuthenticated || typeof dental === 'undefined') { return; }
    var esc = dentalUi.esc;

    // ---- Глобальный поиск пациента ----
    var $search = $('#DentalPatientSearch');
    if ($search.length && dental.patients && dental.patients.patient) {
        var $input = $search.find('input'), $menu = $search.find('.dropdown-menu');
        var run = dentalUi.debounce(function () {
            var q = $input.val().trim();
            if (q.length < 2) { $menu.removeClass('show'); return; }
            dental.patients.patient.getLookup(q, 10, { abpHandleError: false }).then(function (r) {
                if ($input.val().trim() !== q) { return; }
                if (!r.items.length) {
                    $menu.html('<span class="dropdown-item-text text-muted small">' + esc(dentalUi.l('NothingFound')) + '</span>');
                } else {
                    $menu.html(r.items.map(function (p) {
                        return '<a class="dropdown-item" href="' + abp.appPath + 'Patients/Detail?id=' + p.id + '">' +
                            '<div>' + esc(p.fullName) + (p.isVip ? ' <span class="badge bg-warning text-dark">VIP</span>' : '') + '</div>' +
                            '<div class="small text-muted">' + esc(dentalUi.phone(p.phone)) + (p.birthDate ? ' · ' + dentalUi.date(p.birthDate) : '') +
                            (p.balance < 0 ? ' · <span class="text-danger">' + dentalUi.money(p.balance) + '</span>' : '') + '</div></a>';
                    }).join(''));
                }
                $menu.addClass('show');
            });
        }, 250);
        $input.on('input', run);
        $input.on('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                window.location.href = abp.appPath + 'Patients?filter=' + encodeURIComponent($input.val().trim());
            } else if (e.key === 'Escape') { $menu.removeClass('show'); }
        });
        $(document).on('click', function (e) { if (!$search[0].contains(e.target)) { $menu.removeClass('show'); } });
    }

    // ---- Уведомления ----
    var $bell = $('#DentalNotifications');
    if ($bell.length && dental.notifications && dental.notifications.notification) {
        var svc = dental.notifications.notification;
        var render = function (r) {
            var $count = $bell.find('[data-role=count]');
            $count.text(r.unread > 99 ? '99+' : r.unread).toggleClass('d-none', !r.unread);
            var $list = $bell.find('[data-role=list]');
            if (!r.items.length) {
                $list.html('<div class="text-muted small px-3 py-3">' + esc(dentalUi.l('Notifications:Empty')) + '</div>');
                return;
            }
            $list.html(r.items.map(function (n) {
                return '<a href="' + (n.url ? abp.appPath + n.url.replace(/^\//, '') : '#') + '" data-id="' + n.id + '" class="d-block px-3 py-2 border-bottom text-decoration-none ' +
                    (n.readAt ? 'text-muted' : 'text-body bg-light') + '">' +
                    '<div class="small fw-semibold">' + esc(n.title) + '</div>' +
                    (n.body ? '<div class="small">' + esc(n.body) + '</div>' : '') +
                    '<div class="small text-muted">' + dentalUi.dateTime(n.creationTime) + '</div></a>';
            }).join(''));
        };
        var load = function () { svc.getList(false, 15, { abpHandleError: false }).then(render); };
        $bell.on('click', '[data-role=list] a', function () {
            var id = $(this).data('id');
            if (id) { svc.markRead(id, { abpHandleError: false }); }
        });
        $bell.on('click', '[data-role=read-all]', function (e) {
            e.preventDefault();
            e.stopPropagation();
            svc.markAllRead().then(load);
        });
        load();
        setInterval(load, 60000);
    }
});
