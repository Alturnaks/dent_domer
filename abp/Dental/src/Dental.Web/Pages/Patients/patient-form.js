/* Модалка пациента: создание/редактирование, предупреждение о дублях (409 Dental:PatientDuplicate → «Всё равно создать»). */
var patientForm = (function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.patients.patient;
    var esc = dentalUi.esc;
    var $modal, $form, modal, onSaved, refsLoaded = false;

    function init() {
        $modal = $('#PatientFormModal');
        $form = $('#PatientForm');
        if (!$modal.length) { return; }
        modal = bootstrap.Modal.getOrCreateInstance($modal[0]);
        $form.on('submit', function (e) { e.preventDefault(); save(false); });
        $form.find('[data-role=create-anyway]').on('click', function () { save(true); });
    }

    function loadRefs() {
        if (refsLoaded) { return $.Deferred().resolve().promise(); }
        refsLoaded = true;
        return dental.patients.leadSource.getList().then(function (sources) {
            var $s = $form.find('[name=sourceId]');
            (sources.items || []).forEach(function (s) { $s.append($('<option>').val(s.id).text(s.name)); });
            return service.getTags();
        }).then(function (tags) {
            var $dl = $('#PatientTagsList');
            (tags || []).forEach(function (t) { $dl.append($('<option>').val(t)); });
        });
    }

    function fill(p) {
        $form[0].reset();
        $form.find('[data-role=duplicates]').addClass('d-none');
        $form.find('[name=id]').val(p ? p.id : '');
        if (!p) { return; }
        ['lastName', 'firstName', 'middleName', 'iin', 'email', 'address', 'notes'].forEach(function (k) { $form.find('[name=' + k + ']').val(p[k] || ''); });
        $form.find('[name=phone]').val(dentalUi.phone(p.phone));
        $form.find('[name=phoneExtra]').val(dentalUi.phone(p.phoneExtra));
        $form.find('[name=birthDate]').val(p.birthDate ? String(p.birthDate).substr(0, 10) : '');
        $form.find('[name=gender]').val(typeof p.gender === 'number' ? p.gender : ({ Unknown: 0, Male: 1, Female: 2 })[p.gender] || 0);
        $form.find('[name=sourceId]').val(p.sourceId || '');
        $form.find('[name=tags]').val((p.tags || []).join(', '));
        $form.find('[name=isVip]').prop('checked', !!p.isVip);
    }

    function collect(ignoreDuplicates) {
        var v = function (k) { var x = ($form.find('[name=' + k + ']').val() || '').trim(); return x === '' ? null : x; };
        return {
            lastName: v('lastName') || '',
            firstName: v('firstName') || '',
            middleName: v('middleName'),
            birthDate: v('birthDate'),
            gender: parseInt($form.find('[name=gender]').val(), 10) || 0,
            iin: v('iin'),
            phone: v('phone'),
            phoneExtra: v('phoneExtra'),
            email: v('email'),
            address: v('address'),
            sourceId: v('sourceId'),
            notes: v('notes'),
            tags: (v('tags') || '').split(',').map(function (t) { return t.trim(); }).filter(function (t) { return t; }),
            isVip: $form.find('[name=isVip]').is(':checked'),
            ignoreDuplicates: !!ignoreDuplicates
        };
    }

    function showDuplicates(list) {
        var $box = $form.find('[data-role=duplicates]');
        $box.find('[data-role=duplicates-list]').html((list || []).map(function (d) {
            return '<li><a href="' + abp.appPath + 'Patients/Detail?id=' + d.id + '" target="_blank">' + esc(d.fullName) + '</a> — ' +
                esc(dentalUi.phone(d.phone)) + (d.birthDate ? ', ' + dentalUi.date(d.birthDate) : '') +
                ' <span class="text-muted">(' + esc(l('Patient:DuplicateReason:' + d.reason)) + ')</span></li>';
        }).join(''));
        $box.find('[data-role=create-anyway]').toggleClass('d-none', (list || []).some(function (d) { return d.reason === 'iin'; }));
        $box.removeClass('d-none');
        $modal.find('.modal-body').scrollTop(0);
    }

    function save(ignoreDuplicates) {
        var data = collect(ignoreDuplicates);
        if (!data.lastName || !data.firstName) {
            $form.addClass('was-validated');
            return;
        }
        var id = $form.find('[name=id]').val();
        var call = id ? service.update(id, data, { abpHandleError: false }) : service.create(data, { abpHandleError: false });
        var $btn = $form.find('[type=submit]').prop('disabled', true);
        call.then(function (result) {
            modal.hide();
            abp.notify.success(l('SavedSuccessfully'));
            if (onSaved) { onSaved(result); }
        }).catch(function (err) {
            var e = err && (err.error || (err.responseJSON && err.responseJSON.error)) || err;
            if (e && (e.code === 'Dental:PatientDuplicate' || e.code === 'Dental:IinTaken') && e.data && e.data.duplicates) {
                showDuplicates(e.data.duplicates);
                if (e.code === 'Dental:IinTaken') { abp.message.error(e.message); }
                return;
            }
            abp.ajax.showError(e || {});
        }).always(function () { $btn.prop('disabled', false); });
    }

    return {
        /** Открыть форму. patient = null — новый пациент. callback(patientDto) после сохранения. */
        open: function (patient, callback) {
            if (!$modal) { init(); }
            onSaved = callback;
            loadRefs().then(function () {
                fill(patient);
                $modal.find('[data-role=title]').text(patient ? l('Patient:Edit') : l('Patient:New'));
                modal.show();
            });
        }
    };
})();
