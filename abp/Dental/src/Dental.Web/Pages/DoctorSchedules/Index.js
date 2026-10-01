$(function () {
    var l = abp.localization.getResource('Dental');
    var api = dental.schedule.doctorSchedule;
    var chairs = [], generation = 0, loading = false;
    var timezone = abp.setting.get('Dental.Org.Timezone') || 'Asia/Almaty';
    // Keep input dates in the clinic timezone, including when the browser is elsewhere.
    function localDate(date) {
        var parts = new Intl.DateTimeFormat('en-CA', { timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(date);
        function part(type) { return parts.find(function (p) { return p.type === type; }).value; }
        return part('year') + '-' + part('month') + '-' + part('day');
    }
    function localInstant(value) {
        // Resolve the UTC offset for the chosen clinic-local wall time, rather than the browser timezone.
        var guess = new Date(value + 'Z');
        for (var i = 0; i < 3; i++) {
            var parts = new Intl.DateTimeFormat('en-CA', { timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).formatToParts(guess);
            function p(t) { return parts.find(function (x) { return x.type === t; }).value; }
            var wall = Date.parse(p('year') + '-' + p('month') + '-' + p('day') + 'T' + p('hour') + ':' + p('minute') + ':' + p('second') + 'Z');
            guess = new Date(guess.getTime() + Date.parse(value + 'Z') - wall);
        }
        return guess.toISOString();
    }
    function dateTime(value) { return new Intl.DateTimeFormat(abp.localization.currentCulture.name || 'ru',
        { timeZone: timezone, dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)); }
    var today = localDate(new Date());
    $('#ScheduleFrom,#ExceptionFrom,#ExceptionTo').val(today);
    $('#BlockTimezone').text(l('Schedule:Timezone', timezone));
    function branch() { return $('#ScheduleBranch').val(); }
    function doctor() { return $('#ScheduleDoctor').val(); }
    function row(day, enabled, start, end, chairId) {
        var tr = $('<tr>');
        var weekday = $('<select class="form-select weekday">');
        for (var i = 0; i < 7; i++) weekday.append($('<option>').val(i).text(l('Schedule:Day:' + i)));
        weekday.val(day);
        tr.append($('<td>').append(weekday), $('<td>').append($('<input type="checkbox" class="form-check-input enabled">').prop('checked', enabled)),
            $('<td>').append($('<input type="time" class="form-control start">').val((start || '09:00').slice(0, 5))),
            $('<td>').append($('<input type="time" class="form-control end">').val((end || '18:00').slice(0, 5))));
        var select = $('<select class="form-select chair">').append($('<option>').val('').text(l('Schedule:NoChair')));
        chairs.filter(function (c) { return c.isActive; }).forEach(function (c) { select.append($('<option>').val(c.id).text(c.name)); });
        select.val(chairId || '');
        tr.append($('<td>').append(select), $('<td>').append($('<button type="button" class="btn btn-outline-danger">').text(l('Schedule:Delete')).on('click', function () { tr.remove(); })));
        $('#WeekRows').append(tr);
    }
    function refreshDetails() {
        if (!doctor()) return;
        var id = ++generation;
        var from = $('#ScheduleFrom').val();
        if (!from) return;
        var to = (Number(from.slice(0, 4)) + 1) + '-12-31';
        api.getList(branch(), doctor()).then(function (r) {
            if (id !== generation) return;
            $('#WeekRows').empty();
            var shifts = r.items.filter(function (s) { return s.validFrom <= from && (!s.validTo || s.validTo >= from); });
            if (shifts.length) shifts.forEach(function (s) { row(s.weekday, true, s.startTime, s.endTime, s.chairId); });
            else for (var d = 1; d <= 7; d++) row(d % 7, false);
        });
        api.getExceptions(doctor(), from, to).then(function (r) {
            if (id !== generation) return;
            $('#ExceptionRows').empty();
            r.items.forEach(function (e) {
                var tr = $('<tr>').append($('<td>').text(l('Schedule:ExceptionType:' + e.type)), $('<td>').text(e.dateFrom + ' — ' + e.dateTo),
                    $('<td>').text(e.startTime ? e.startTime.slice(0, 5) + ' — ' + e.endTime.slice(0, 5) : l('Schedule:WholeDay')),
                    $('<td>').text(e.comment || ''));
                tr.append($('<td>').append($('<button class="btn btn-outline-danger">').text(l('Schedule:Delete')).on('click', function () {
                    abp.message.confirm(l('Schedule:DeleteConfirm')).then(function (yes) { if (yes) api.deleteException(e.id).then(refreshDetails); });
                })));
                $('#ExceptionRows').append(tr);
            });
        });
        api.getBlocks(branch(), localInstant(from + 'T00:00'), localInstant(to + 'T00:00')).then(function (r) {
            if (id !== generation) return;
            $('#BlockRows').empty();
            r.items.filter(function (b) { return !b.doctorId || b.doctorId === doctor(); }).forEach(function (b) {
                var tr = $('<tr>').append($('<td>').text(dateTime(b.startsAt) + ' — ' + dateTime(b.endsAt)), $('<td>').text(b.reason || ''));
                tr.append($('<td>').append($('<button class="btn btn-outline-danger">').text(l('Schedule:Delete')).on('click', function () {
                    abp.message.confirm(l('Schedule:DeleteConfirm')).then(function (yes) { if (yes) api.deleteBlock(b.id).then(refreshDetails); });
                })));
                $('#BlockRows').append(tr);
            });
        });
    }
    function loadBranch() {
        loading = true; generation++;
        $('#SaveWeek,#AddShift,#ExceptionForm button,#BlockForm button').prop('disabled', true);
        $('#ScheduleDoctor,#WeekRows,#ExceptionRows,#BlockRows').empty();
        if (!branch()) return;
        var selectedBranch = branch();
        $.when(dental.staff.employee.getLookup({ branchId: selectedBranch }), dental.branches.branch.getChairs(selectedBranch)).then(function (doctors, resources) {
            if (selectedBranch !== branch()) return;
            chairs = resources.items;
            doctors.items.forEach(function (d) { $('#ScheduleDoctor').append($('<option>').val(d.id).text(d.fullName)); });
            loading = false;
            $('#ScheduleEmpty').toggleClass('d-none', !!doctor());
            $('#SaveWeek,#AddShift,#ExceptionForm button,#BlockForm button').prop('disabled', !doctor());
            refreshDetails();
        });
    }
    $('#ScheduleBranch').on('change', loadBranch);
    $('#ScheduleDoctor,#ScheduleFrom').on('change', refreshDetails);
    $('#AddShift').on('click', function () { row(1, true); });
    $('#SaveWeek').on('click', function () {
        if (loading || !doctor()) return;
        var days = [], invalid = false;
        $('#WeekRows tr').each(function () {
            var tr = $(this);
            if (!tr.find('.enabled').is(':checked')) return;
            var start = tr.find('.start').val(), end = tr.find('.end').val();
            if (!start || !end || start >= end) invalid = true;
            days.push({ weekday: Number(tr.find('.weekday').val()), startTime: start + ':00', endTime: end + ':00', chairId: tr.find('.chair').val() || null });
        });
        if (invalid || !$('#ScheduleFrom').val()) { abp.message.warn(l('Dental:ScheduleInvalidRange')); return; }
        abp.message.confirm(l('Schedule:ReplaceConfirm')).then(function (yes) {
            if (!yes) return;
            $('#SaveWeek').prop('disabled', true);
            api.replaceWeek({ branchId: branch(), doctorId: doctor(), validFrom: $('#ScheduleFrom').val(), days: days })
                .then(function () { abp.notify.success(l('Schedule:Saved')); refreshDetails(); })
                .always(function () { $('#SaveWeek').prop('disabled', false); });
        });
    });
    $('#ExceptionForm').on('submit', function (event) {
        event.preventDefault();
        var start = $('#ExceptionStart').val(), end = $('#ExceptionEnd').val();
        api.createException({ doctorId: doctor(), branchId: branch(), dateFrom: $('#ExceptionFrom').val(), dateTo: $('#ExceptionTo').val(),
            type: Number($('#ExceptionType').val()), startTime: start ? start + ':00' : null, endTime: end ? end + ':00' : null,
            comment: $('#ExceptionComment').val() }).then(refreshDetails);
    });
    $('#BlockForm').on('submit', function (event) {
        event.preventDefault();
        api.createBlock({ branchId: branch(), doctorId: doctor(), startsAt: localInstant($('#BlockStart').val()),
            endsAt: localInstant($('#BlockEnd').val()), reason: $('#BlockReason').val() }).then(refreshDetails);
    });
    dental.branches.branch.getLookup().then(function (r) {
        r.items.forEach(function (b) { $('#ScheduleBranch').append($('<option>').val(b.id).text(b.name)); });
        loadBranch();
    });
});
