$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.patients.patient;
    var esc = dentalUi.esc;
    var detailUrl = function (id) { return abp.appPath + 'Patients/Detail?id=' + id; };

    dental.patients.leadSource.getList().then(function (r) {
        r.items.forEach(function (s) { $('#PatientSourceFilter').append($('<option>').val(s.id).text(s.name)); });
    });
    service.getTags().then(function (tags) {
        tags.forEach(function (t) { $('#PatientTagFilter').append($('<option>').val(t).text(t)); });
    });

    var dataTable = $('#PatientsTable').DataTable(
        abp.libs.datatables.normalizeConfiguration({
            serverSide: true,
            paging: true,
            searching: false,
            scrollX: true,
            order: [[6, 'desc']],
            ajax: abp.libs.datatables.createAjax(service.getList, function () {
                var months = parseInt($('#PatientNotVisitedFilter').val(), 10);
                return {
                    filter: $('#PatientFilter').val(),
                    sourceId: $('#PatientSourceFilter').val() || null,
                    tag: $('#PatientTagFilter').val() || null,
                    debtors: $('#PatientDebtorsFilter').is(':checked') ? true : null,
                    isVip: $('#PatientVipFilter').is(':checked') ? true : null,
                    notVisitedMonths: months > 0 ? months : null
                };
            }),
            columnDefs: [
                {
                    title: l('Actions'),
                    rowAction: {
                        items: [
                            { text: l('Patient:Card'), action: function (data) { window.location.href = detailUrl(data.record.id); } },
                            {
                                text: l('Edit'),
                                visible: abp.auth.isGranted('Dental.Patients.Edit'),
                                action: function (data) {
                                    service.get(data.record.id).then(function (p) { patientForm.open(p, function () { dataTable.ajax.reload(null, false); }); });
                                }
                            }
                        ]
                    }
                },
                {
                    title: l('Patient:FullName'), data: 'fullName', orderable: true, name: 'fullName',
                    render: function (v, t, r) {
                        var tags = (r.tags || []).map(function (x) { return '<span class="badge bg-light text-dark border me-1">' + esc(x) + '</span>'; }).join('');
                        return '<a href="' + detailUrl(r.id) + '">' + esc(v) + '</a>' +
                            (r.isVip ? ' <span class="badge bg-warning text-dark">VIP</span>' : '') +
                            (tags ? '<div>' + tags + '</div>' : '');
                    }
                },
                {
                    title: l('Patient:BirthDate'), data: 'birthDate', name: 'birthDate',
                    render: function (v) { return v ? dentalUi.date(v) + ' <span class="text-muted small">(' + dentalUi.age(v) + ')</span>' : ''; }
                },
                { title: l('Patient:Phone'), data: 'phone', orderable: false, render: function (v) { return esc(dentalUi.phone(v)); } },
                { title: l('Patient:Iin'), data: 'iin', orderable: false, render: function (v) { return esc(v); } },
                { title: l('Patient:Balance'), data: 'balance', orderable: false, className: 'text-end', render: function (v) { return dentalUi.balance(v); } },
                { title: l('Patient:CreatedAt'), data: 'creationTime', name: 'creationTime', render: function (v) { return dentalUi.date(v); } },
                { title: l('Patient:LastVisit'), data: 'lastVisitAt', orderable: false, render: function (v) { return dentalUi.date(v); } },
                { title: l('Patient:Source'), data: 'sourceName', orderable: false, render: function (v) { return esc(v); } }
            ]
        })
    );

    var reload = function () { dataTable.ajax.reload(); };
    $('#PatientFilter').on('input', dentalUi.debounce(reload, 350));
    $('#PatientSourceFilter, #PatientTagFilter, #PatientDebtorsFilter, #PatientVipFilter').on('change', reload);
    $('#PatientNotVisitedFilter').on('input', dentalUi.debounce(reload, 400));
    $('#PatientFiltersReset').on('click', function () {
        $('#PatientFilters').find('input[type=search], input[type=number]').val('');
        $('#PatientFilters').find('select').val('');
        $('#PatientFilters').find('input[type=checkbox]').prop('checked', false);
        reload();
    });

    $('#NewPatientButton').on('click', function (e) {
        e.preventDefault();
        patientForm.open(null, function (p) { window.location.href = detailUrl(p.id); });
    });
});
