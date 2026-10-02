$(function () {
    var root = $('#PatientDetail'), patient = root.data('id'), service = dental.patients.patientFile;
    var l = abp.localization.getResource('Dental'), form = $('#PatientFileForm');
    function error(message) { $('#PatientFileError').removeClass('d-none').text(message); }
    function reload() {
        return service.getList(patient).then(function (rows) {
            var body = $('#PatientFileRows').empty();
            rows.forEach(function (file) {
                var download = $('<button type="button" class="btn btn-link">').text(file.fileName).on('click', function () {
                    service.getDownload(file.id).then(function (url) { window.location.assign(url); });
                });
                var row = $('<tr>').append($('<td>').append(download), $('<td>').text((file.size / 1024).toFixed(1) + ' KB'),
                    $('<td>').text(dentalUi.dateTime(file.creationTime)));
                if (root.data('can-edit')) {
                    row.append($('<td>').append($('<button type="button" class="btn btn-outline-danger btn-sm">').text(l('Delete')).on('click', function () {
                        abp.message.confirm(l('Patient:DeleteFile')).then(function (yes) { if (yes) service.delete(file.id).then(reload); });
                    })));
                }
                body.append(row);
            });
            if (!rows.length) body.append($('<tr>').append($('<td>').text(l('Patient:NoFiles'))));
        });
    }
    reload();
    form.on('submit', async function (event) {
        event.preventDefault(); $('#PatientFileError').addClass('d-none');
        var file = $('#PatientFileInput')[0].files[0];
        if (!file || file.size < 1 || file.size > 20 * 1024 * 1024) { error(l('Patient:FilesHint')); return; }
        form.find('button').prop('disabled', true);
        try {
            var ticket = await service.createUpload({ patientId: patient, fileName: file.name, contentType: file.type, size: file.size });
            var data = new FormData();
            Object.keys(ticket.upload.fields).forEach(function (key) { data.append(key, ticket.upload.fields[key]); });
            data.append('file', file);
            var result = await fetch(ticket.upload.url, { method: 'POST', body: data, credentials: 'omit' });
            if (!result.ok) throw new Error(l('Patient:UploadFailed'));
            await service.complete(ticket.id); $('#PatientFileInput').val(''); await reload();
        } catch (e) { error(e.message || l('Patient:UploadFailed')); }
        finally { form.find('button').prop('disabled', false); }
    });
});
