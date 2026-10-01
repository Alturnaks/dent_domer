// ABP date inputs may return a localized value after the date picker is initialized.
var dentalCalendarDates = (function () {
    function normalize(value) {
        value = String(value || '').trim();
        var local = /^(\d{2})\.(\d{2})\.(\d{4})$/.exec(value);
        if (local) value = local[3] + '-' + local[2] + '-' + local[1];
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) throw new RangeError('Invalid calendar date');
        var parsed = new Date(value + 'T12:00:00Z');
        if (!Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== value) throw new RangeError('Invalid calendar date');
        return value;
    }
    function addDays(value, delta) {
        var date = new Date(normalize(value) + 'T12:00:00Z');
        date.setUTCDate(date.getUTCDate() + delta);
        return date.toISOString().slice(0, 10);
    }
    return { normalize: normalize, addDays: addDays };
})();
