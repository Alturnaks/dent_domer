const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const context = vm.createContext({});
vm.runInContext(fs.readFileSync(path.join(__dirname, '../abp/Dental/src/Dental.Web/Pages/Schedule/calendar-date.js'), 'utf8'), context);
const dates = context.dentalCalendarDates;
assert.equal(dates.normalize('06.10.2026'), '2026-10-06');
assert.equal(dates.normalize('2026-10-06'), '2026-10-06');
assert.equal(dates.normalize(' 31.12.2026 '), '2026-12-31');
assert.equal(dates.addDays('31.12.2026', 1), '2027-01-01');
assert.equal(dates.addDays('01.10.2026', -1), '2026-09-30');
assert.equal(dates.addDays('06.10.2026', 6), '2026-10-12');
assert.equal(dates.normalize('29.02.2028'), '2028-02-29');
for (const invalid of ['29.02.2026', '31.04.2026', '', '06/10/2026']) {
    assert.throws(() => dates.normalize(invalid), /Invalid calendar date/);
}
assert.equal(dates.startOfWeek('06.10.2026'), '2026-10-05');
assert.equal(dates.startOfWeek('11.10.2026'), '2026-10-05');
assert.equal(dates.startOfWeek('05.10.2026'), '2026-10-05');
assert.equal(dates.startOfWeek('01.01.2027'), '2026-12-28');
console.log('Calendar dates: 15 checks passed');
