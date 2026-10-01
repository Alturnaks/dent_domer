$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.patients.patient;
    var esc = dentalUi.esc;
    var $root = $('#PatientDetail');
    var id = $root.data('id');

    $root.find('[data-role=phone]').each(function () { $(this).text(dentalUi.phone($(this).data('phone')) || '—'); });
    $root.find('[data-role=age]').each(function () { $(this).text(' (' + dentalUi.age($(this).data('birth')) + ')'); });
    $root.find('[data-role=date]').each(function () { var v = $(this).data('value'); if (v) { $(this).text(dentalUi.date(v)); } });
    var next = $root.find('[data-role=next]').data('value');
    if (next) { $root.find('[data-role=next]').text(dentalUi.dateTime(next)); }

    service.getBalance(id).then(function (b) {
        $root.find('[data-role=balance]').html(dentalUi.balance(b.balance));
        $root.find('[data-role=paid]').text(dentalUi.money(b.totalPaid));
    });

    $('#EditPatientButton').on('click', function () {
        service.get(id).then(function (p) { patientForm.open(p, function () { window.location.reload(); }); });
    });

    // Согласия
    var loadConsents = function () {
        service.getConsents(id).then(function (r) {
            $('#ConsentsBody').html(r.items.length ? r.items.map(function (c) {
                return '<tr><td>' + esc(c.type) + '</td><td>' + dentalUi.date(c.signedAt) + '</td><td>' +
                    (c.fileUrl ? '<a href="' + esc(c.fileUrl) + '" target="_blank" rel="noopener">' + esc(c.fileUrl) + '</a>' : '') + '</td></tr>';
            }).join('') : '<tr><td colspan="3" class="text-muted">' + esc(l('Patient:NoConsents')) + '</td></tr>');
        });
    };
    loadConsents();
    l('Patient:ConsentTypes').split('|').forEach(function (t) { $('#ConsentTypes').append($('<option>').val(t)); });
    $('#ConsentForm').on('submit', function (e) {
        e.preventDefault();
        var $f = $(this);
        var type = $f.find('[name=type]').val().trim();
        if (!type) { return; }
        service.addConsent(id, {
            type: type,
            signedAt: $f.find('[name=signedAt]').val() || null,
            fileUrl: $f.find('[name=fileUrl]').val().trim() || null
        }).then(function () { $f[0].reset(); abp.notify.success(l('SavedSuccessfully')); loadConsents(); });
    });
});
