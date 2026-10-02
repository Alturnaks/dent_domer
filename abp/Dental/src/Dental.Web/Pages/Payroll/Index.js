$(function () {
    'use strict';
    const api = dental.payroll.payroll, manage = abp.auth.isGranted('Dental.Payroll.Manage');
    const statuses = ['Черновик', 'Утверждена', 'Выплачена'], schemes = ['Процент выручки', 'Процент за вычетом материалов', 'Оклад + процент', 'За рабочий день'];
    const money = n => (Number(n || 0) / 100).toLocaleString('ru-RU', {minimumFractionDigits: 2});
    const text = value => $('<span>').text(value == null ? '' : value);
    const cell = (row, value) => row.append($('<td>').append(text(value)));
    let current, employees = [], submit;
    const branch = () => $('#PayrollBranch').val();
    const modal = new bootstrap.Modal(document.getElementById('PayrollModal'));
    function button(label, action) { return $('<button type="button" class="btn btn-sm btn-outline-primary me-1">').text(label).on('click', () => Promise.resolve().then(action).catch(error)); }
    function error(e) { abp.message.error(e.message || (e.responseJSON && e.responseJSON.error && e.responseJSON.error.message) || 'Не удалось выполнить действие.'); }
    function field(name, label, type, value) { const wrap = $('<div class="mb-3">').append($('<label class="form-label">').attr('for', name).text(label)); const input = $('<input class="form-control" required>').attr({id: name, name: name, type: type || 'text'}).val(value == null ? '' : value); if (type === 'number') input.attr({min: 0, step: '0.01'}); wrap.append(input); $('#PayrollFields').append(wrap); return input; }
    function select(name, label, rows) { $('#PayrollFields').append($('<label class="form-label">').attr('for', name).text(label)); const input = $('<select class="form-select mb-3" required>').attr({id:name,name:name}); rows.forEach(r => input.append($('<option>').val(r.id).text(r.name))); $('#PayrollFields').append(input); return input; }
    function open(title, action) { $('#PayrollFields').empty(); $('#PayrollModalTitle').text(title); submit = action; modal.show(); }
    function amount(name) { return Math.round(Number($('#' + name).val()) * 100); }
    async function load() {
        if (!branch()) return;
        $('#PayrollDetail').prop('hidden', true);
        const values = await Promise.all([api.getPeriods(branch()),api.getSchemes(branch()),api.getEmployees(branch())]); employees = values[2];
        $('#PayrollPeriods').empty(); values[0].forEach(p => { const row = $('<tr>'); cell(row, p.start + ' — ' + p.end); cell(row,statuses[p.status]); cell(row,money(p.total)); row.append($('<td>').append(button('Открыть', () => detail(p.id)))); $('#PayrollPeriods').append(row); });
        $('#PayrollSchemes').empty(); values[1].forEach(s => { const row = $('<tr>'); [s.employeeName,s.validFrom,schemes[s.type],s.percent,money(s.fixedAmount),money(s.shiftRate)].forEach(v => cell(row,v)); $('#PayrollSchemes').append(row); });
    }
    async function detail(id) {
        current = await api.get(id); const p = current.period;
        $('#PayrollDetail').prop('hidden',false); $('#PayrollTitle').text(p.start + ' — ' + p.end + ': ' + statuses[p.status]); $('#PayrollActions,#PayrollEntries,#PayrollVisits').empty();
        if (manage && p.status === 0) {
            $('#PayrollActions').append(button('Пересчитать',async () => { await api.recalculate(id,{concurrencyStamp:p.concurrencyStamp}); await load(); await detail(id); }),button('Утвердить',async () => { if (await abp.message.confirm('Утвердить ведомость? После этого суммы будут зафиксированы.')) { await api.approve(id,{concurrencyStamp:p.concurrencyStamp}); await load(); await detail(id); } }));
        }
        if (manage && p.status === 1) $('#PayrollActions').append(button('Отметить выплату',async () => { if (await abp.message.confirm('Зарплата выплачена сотрудникам?')) { await api.markPaid(id,{concurrencyStamp:p.concurrencyStamp}); await load(); await detail(id); } }));
        current.entries.forEach(e => {
            const row = $('<tr>'); [e.employeeName,money(e.baseRevenue),money(e.materialsCost),money(e.accrued),money(e.bonus),money(e.penalty),money(e.total)].forEach(v => cell(row,v));
            const actions = $('<td>').append(button('Детали', () => {
                $('#PayrollVisits').empty().append($('<h5>').text(e.employeeName)); if (e.comment) $('#PayrollVisits').append($('<p>').text(e.comment));
                const table = $('<table class="table">').append('<thead><tr><th>Дата</th><th>Выручка, ₸</th><th>Материалы, ₸</th><th>Оплачен</th><th></th></tr></thead>'); const body = $('<tbody>'); e.details.forEach(d => { const r = $('<tr>'); [d.date,money(d.revenue),money(d.materialsCost),d.paid?'Да':'Нет'].forEach(v => cell(r,v)); r.append($('<td>').append($('<a>').attr('href','/Visits/Detail?id='+encodeURIComponent(d.visitId)).text('Визит'))); body.append(r); }); $('#PayrollVisits').append(table.append(body));
            }));
            if (manage && p.status === 0) actions.append(button('Бонус / штраф',() => { open('Корректировка начисления',async () => { await api.adjust(e.id,{bonus:amount('Bonus'),penalty:amount('Penalty'),comment:$('#Comment').val(),concurrencyStamp:e.concurrencyStamp}); await load(); await detail(id); }); field('Bonus','Бонус, ₸','number',e.bonus/100); field('Penalty','Штраф, ₸','number',e.penalty/100); field('Comment','Причина','text',e.comment); }));
            row.append(actions); $('#PayrollEntries').append(row);
        });
    }
    $('#PayrollCreate').on('click',() => { const now = new Date(); const iso = d => d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0'); open('Новая ведомость', async () => { const result = await api.create({branchId:branch(),start:dentalCalendarDates.normalize($('#Start').val()),end:dentalCalendarDates.normalize($('#End').val())}); await load(); await detail(result.period.id); }); field('Start','Начало','date',iso(new Date(now.getFullYear(),now.getMonth(),1))); field('End','Конец','date',iso(new Date(now.getFullYear(),now.getMonth()+1,0))); });
    $('#PayrollScheme').on('click',() => { open('Новая версия схемы',async () => { await api.saveScheme({employeeId:$('#Employee').val(),type:Number($('#Type').val()),percent:Number($('#Percent').val()),fixedAmount:amount('Fixed'),shiftRate:amount('Rate'),validFrom:dentalCalendarDates.normalize($('#ValidFrom').val())}); await load(); }); select('Employee','Сотрудник',employees); select('Type','Схема',schemes.map((name,id)=>({id,name}))); field('Percent','Процент','number',0).attr('max',100); field('Fixed','Оклад, ₸','number',0); field('Rate','За рабочий день, ₸','number',0); field('ValidFrom','Действует с','date',new Date().toISOString().slice(0,10)); });
    $('#PayrollForm').on('submit',async e => { e.preventDefault(); const b = $('#PayrollForm button[type=submit]').prop('disabled',true); try { await submit(); modal.hide(); } catch (ex) { error(ex); } finally { b.prop('disabled',false); } });
    $('#PayrollManage').toggleClass('d-none',!manage); $('#PayrollBranch').on('change',()=>load().catch(error));
    dental.branches.branch.getLookup().then(async result => { (result.items || result).forEach(b => $('#PayrollBranch').append($('<option>').val(b.id).text(b.name))); const id=new URLSearchParams(location.search).get('periodId'); if(id){const p=await api.get(id);$('#PayrollBranch').val(p.period.branchId);} await load();if(id)await detail(id); }).catch(error);
});
