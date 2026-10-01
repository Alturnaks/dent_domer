$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.patients.patient;
    var esc = dentalUi.esc;
    var pairs = [];

    function card(c) {
        return '<a href="' + abp.appPath + 'Patients/Detail?id=' + c.id + '" target="_blank" class="fw-semibold">' + esc(c.fullName) + '</a>' +
            '<div class="small text-muted">' + esc(dentalUi.phone(c.phone)) + (c.birthDate ? ' · ' + dentalUi.date(c.birthDate) : '') +
            (c.iin ? ' · ' + l('Patient:Iin') + ' ' + esc(c.iin) : '') + '</div>';
    }

    function load() {
        service.getDuplicates().then(function (r) {
            pairs = r.items;
            if (!pairs.length) {
                $('#DuplicatesList').html('<div class="text-muted">' + esc(l('Patient:NoDuplicates')) + '</div>');
                return;
            }
            $('#DuplicatesList').html('<table class="table align-middle"><tbody>' + pairs.map(function (p, i) {
                return '<tr><td style="width:35%">' + card(p.a) + '</td><td style="width:35%">' + card(p.b) + '</td>' +
                    '<td><span class="badge bg-light text-dark border">' + esc(l('Patient:DuplicateReason:' + p.reason)) + '</span></td>' +
                    '<td class="text-end text-nowrap">' +
                    '<button class="btn btn-sm btn-outline-primary me-1" data-i="' + i + '" data-keep="a">' + esc(l('Patient:KeepA')) + '</button>' +
                    '<button class="btn btn-sm btn-outline-primary" data-i="' + i + '" data-keep="b">' + esc(l('Patient:KeepB')) + '</button></td></tr>';
            }).join('') + '</tbody></table>');
        });
    }

    $('#DuplicatesList').on('click', 'button[data-i]', function () {
        var p = pairs[$(this).data('i')];
        var keepA = $(this).data('keep') === 'a';
        var main = keepA ? p.a : p.b, dup = keepA ? p.b : p.a;
        abp.message.confirm(l('Patient:MergeConfirm', main.fullName, dup.fullName)).then(function (ok) {
            if (!ok) { return; }
            service.merge({ mainId: main.id, duplicateId: dup.id }).then(function () {
                abp.notify.success(l('Patient:MergedSuccessfully'));
                load();
            });
        });
    });

    load();
});
