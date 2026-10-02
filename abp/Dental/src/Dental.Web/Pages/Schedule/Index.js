$(function () {
    var l = abp.localization.getResource('Dental'), api = dental.schedule.appointment;
    var manage = abp.auth.isGranted('Dental.Schedule.Manage'), force = abp.auth.isGranted('Dental.Schedule.DoctorSchedulesManage');
    var timezone = abp.setting.get('Dental.Org.Timezone') || 'Asia/Almaty';
    var data, chairs = [], doctors = [], serviceRows = [], current = null, generation = 0, patientGeneration = 0, saving = false, waitingMode = false, linkedAppointment = null;
    var modal = bootstrap.Modal.getOrCreateInstance(document.getElementById('AppointmentModal'));
    function parts(value) {
        var list = new Intl.DateTimeFormat('en-CA', {timeZone: timezone, year:'numeric', month:'2-digit', day:'2-digit', hour:'2-digit', minute:'2-digit', hourCycle:'h23'}).formatToParts(new Date(value));
        function p(t) {return list.find(function(x){return x.type === t;}).value;}
        return {date:p('year')+'-'+p('month')+'-'+p('day'), time:p('hour')+':'+p('minute'), minute:Number(p('hour'))*60+Number(p('minute'))};
    }
    function instant(value) {
        var target = Date.parse(value+'Z'), guess = new Date(target);
        for (var i=0;i<4;i++) {var wall=parts(guess); guess=new Date(guess.getTime()+target-Date.parse(wall.date+'T'+wall.time+':00Z'));}
        return guess.toISOString();
    }
    function calendarDate() {return dentalCalendarDates.normalize($('#CalendarDate').val());}
    function addDays(date, delta) {return dentalCalendarDates.addDays(date,delta);}
    function wall(date, minute) {date=addDays(date,Math.floor(minute/1440));minute=minute%1440;return date+'T'+String(Math.floor(minute/60)).padStart(2,'0')+':'+String(minute%60).padStart(2,'0');}
    function branch() {return $('#CalendarBranch').val();}
    function fill(select, rows, empty) {var s=$(select).empty();if(empty) s.append($('<option>').val('').text(empty));rows.forEach(function(r){s.append($('<option>').val(r.id).text(r.name||r.fullName));});}
    function statusName(status) {return l('Calendar:Status:'+status);}
    function doctorFilterOptions() {
        var select=$('#CalendarDoctor'),selected=select.val(),week=$('#CalendarView').val()==='week';
        fill(select,doctors,week?null:l('Calendar:AllDoctors'));
        select.val(doctors.some(function(d){return d.id===selected;})?selected:(week?doctors[0]?.id:''));
    }
    function load() {
        if (!branch() || !$('#CalendarDate').val()) return;
        var n=++generation, date=calendarDate(), week=$('#CalendarView').val()==='week';
        if(week) date=dentalCalendarDates.startOfWeek(date);
        api.getCalendar({branchId:branch(),from:date,to:week?addDays(date,6):date,doctorId:$('#CalendarDoctor').val()||null}).then(function(r){
            if(n!==generation)return;data=r;timezone=r.timezone;$('#CalendarTimezone').text(l('Schedule:Timezone',timezone));render();
            if(linkedAppointment){var linked=linkedAppointment;linkedAppointment=null;open(linked);}
        });
        if(manage) api.getWaitlist(branch()).then(function(r){if(n===generation)renderWaitlist(r.items);});
    }
    function render() {
        var grid=$('#CalendarGrid').empty(), date=calendarDate(), mode=$('#CalendarView').val(), columns=[];
        if(mode==='week') {
            date=dentalCalendarDates.startOfWeek(date);
            for(var d=0;d<7;d++)columns.push({date:addDays(date,d),id:$('#CalendarDoctor').val()});
        }
        else (mode==='chairs'?chairs.concat([{id:null,name:l('Schedule:NoChair')}]):data.doctors).forEach(function(r){columns.push({date:date,id:r.id,name:r.name});});
        $('#CalendarEmpty').toggleClass('d-none',columns.length>0);
        if(!columns.length)return;
        var start=8*60,end=20*60;
        data.appointments.concat(data.working.map(function(w){return {startsAt:w.startsAt,endsAt:w.endsAt};})).forEach(function(a){
            var first=parts(a.startsAt),last=parts(a.endsAt);start=Math.min(start,Math.floor(first.minute/60)*60);end=Math.max(end,last.date>first.date?1440:Math.ceil(last.minute/60)*60);
        });
        var scale=dentalCalendarLayout.pixelsPerMinute;
        end=Math.min(1440,Math.max(end,start+60));var height=(end-start)*scale;
        grid[0].style.setProperty('--calendar-hour-height',(60*scale)+'px');
        grid[0].style.setProperty('--calendar-half-height',(30*scale)+'px');
        grid[0].style.setProperty('--calendar-slot-height',(data.slotMinutes*scale)+'px');
        var hours=$('<div class="calendar-column calendar-hours">').append($('<div class="calendar-header">'));
        var ticks=$('<div class="calendar-track">').height(height);
        for(var m=start;m<end;m+=30)ticks.append($('<span>').toggleClass('half-hour',m%60!==0).css('top',(m-start)*scale).text(wall(date,m).slice(-5)));
        grid.append(hours.append(ticks));
        function range(item,column){var a=parts(item.startsAt),b=parts(item.endsAt);if(a.date>column.date||b.date<column.date)return null;
            var lo=a.date<column.date?0:a.minute,hi=b.date>column.date?1440:b.minute;
            return dentalCalendarLayout.position(lo,hi,start,end);}
        columns.forEach(function(c){
            var header=$('<div class="calendar-header">');
            if(mode==='week') {
                var dayDate=new Date(c.date+'T12:00:00Z'),culture=abp.localization.currentCulture.name||'ru';
                header.append($('<span class="calendar-weekday">').text(new Intl.DateTimeFormat(culture,{weekday:'short',timeZone:'UTC'}).format(dayDate)),
                    $('<span class="calendar-day">').text(new Intl.DateTimeFormat(culture,{day:'2-digit',month:'short',timeZone:'UTC'}).format(dayDate)));
            } else header.text(c.name);
            var col=$('<div class="calendar-column">').append(header);
            col.toggleClass('calendar-today',c.date===parts(new Date()).date);
            var track=$('<div class="calendar-track">').height(height);
            data.working.filter(function(w){return mode!=='chairs'&&w.doctorId===c.id;}).forEach(function(w){var r=range(w,c);if(r)track.append($('<div class="calendar-working">').css(r));});
            data.blocks.filter(function(b){return (!b.doctorId&&!b.chairId)||(mode==='chairs'?b.chairId===c.id:b.doctorId===c.id);}).forEach(function(b){var r=range(b,c);if(r)track.append($('<div class="calendar-block">').css(r).attr('title',b.reason||''));});
            var appointments=data.appointments.filter(function(a){return (a.status!==5||$('#ShowCancelled').is(':checked'))&&(mode==='chairs'?a.chairId===c.id:a.doctorId===c.id)&&range(a,c);});
            var lanes=dentalCalendarLayout.lanes(appointments.map(function(a){var r=range(a,c);return {id:a.id,start:r.top,end:r.top+r.height};}));
            appointments.forEach(function(a){
                var r=range(a,c);if(!r)return;
                var doc=data.doctors.find(function(d){return d.id===a.doctorId;});
                var lane=lanes[a.id],width=100/lane.count;
                r.left='calc('+(lane.lane*width)+'% + 4px)';r.width='calc('+width+'% - 8px)';r.right='auto';
                var time=parts(a.startsAt).time+'–'+parts(a.endsAt).time,serviceText=a.services.map(function(s){return s.name;}).join(', ');
                var description=time+' · '+a.patientName+' · '+statusName(a.status)+(serviceText?' · '+serviceText:'');
                var button=$('<button type="button" class="calendar-appointment">').addClass('status-'+a.status).css(r)
                    .append($('<span class="calendar-event-time">').text(time),$('<span class="calendar-event-patient">').text(a.patientName))
                    .attr('title',description).attr('aria-label',description)
                    .on('click',function(e){e.stopPropagation();open(a);});
                if(r.height>=60)button.append($('<span class="calendar-event-details">').text(serviceText||statusName(a.status)));
                if(doc&&/^#[0-9a-f]{6}$/i.test(doc.color||''))button[0].style.setProperty('--doctor-color',doc.color);
                if(manage&&(a.status===0||a.status===1))button.attr('draggable','true').on('dragstart',function(e){e.originalEvent.dataTransfer.setData('text/plain',a.id);});
                if(manage&&(a.status===0||a.status===1)&&parts(a.startsAt).date===c.date&&parts(a.endsAt).date===c.date){
                    var handle=$('<span class="calendar-resize-handle">').attr({'aria-label':l('Calendar:Resize'),'title':l('Calendar:Resize')});
                    handle.on('click',function(e){e.stopPropagation();}).on('pointerdown',function(e){
                        e.preventDefault();e.stopPropagation();var element=this,pointer=e.originalEvent.pointerId,originalHeight=r.height,newEnd=parts(a.endsAt).minute,changed=false;
                        button.attr('draggable','false');element.setPointerCapture(pointer);
                        function move(event){var y=event.originalEvent.clientY-track[0].getBoundingClientRect().top;newEnd=dentalCalendarLayout.resizeEnd(y,start,end,parts(a.startsAt).minute,data.slotMinutes);changed= newEnd!==parts(a.endsAt).minute;button.css('height',Math.max(18,(newEnd-parts(a.startsAt).minute)*scale));}
                        function done(event){handle.off('pointermove',move).off('pointerup',done).off('pointercancel',cancel);button.attr('draggable','true').css('height',originalHeight);if(changed){open(a);$('#AppointmentEnd').val(wall(c.date,newEnd));}}
                        function cancel(){changed=false;done();}
                        handle.on('pointermove',move).on('pointerup',done).on('pointercancel',cancel);
                    });button.append(handle);
                }
                track.append(button);
            });
            function minuteAt(event){return dentalCalendarLayout.minuteAt(event.clientY-track[0].getBoundingClientRect().top,start,end,data.slotMinutes);}
            track.on('click',function(e){if(manage&&e.target===track[0])open(null,c,minuteAt(e));});
            track.on('dragover',function(e){if(manage)e.preventDefault();}).on('drop',function(e){
                if(!manage)return;e.preventDefault();var id=e.originalEvent.dataTransfer.getData('text/plain'),a=data.appointments.find(function(x){return x.id===id;});if(!a)return;
                open(a,c,minuteAt(e.originalEvent));
            });grid.append(col.append(track));
        });
    }
    function open(row,column,minute,waiting) {
        waitingMode=!!waiting;
        current=row;$('#AvailableSlots,#StatusActions').empty();$('#AppointmentForce').prop('checked',false);
        $('#MoveReason,#CancelReason').val('');$('#CancelComment').val('');
        $('.new-fields').toggle(!row);$('.existing-fields').toggle(!!row);$('#AppointmentTitle').text(row?row.patientName:l('Calendar:New'));
        $('#SaveAppointment').toggle(manage&&(!row||row.status===0||row.status===1)).text(row?l('Calendar:Move'):l('Calendar:Save'));
        $('#FindSlots').toggle(manage);$('#ForcePanel').toggle(force);$('#QuickPatient').toggle(abp.auth.isGranted('Dental.Patients.Create'));
        $('.waitlist-fields').toggle(waitingMode);
        $('#AppointmentStart,#AppointmentEnd').prop('required',!waitingMode).closest('.col-md-6').toggle(!waitingMode);
        $('#AppointmentChair').closest('.col-md-6').toggle(!waitingMode);
        $('#AppointmentServices').closest('.col-md-6').toggle(!waitingMode&&!row);
        if(waitingMode){$('#AppointmentTitle').text(l('Calendar:AddWaitlist'));$('#FindSlots,#ForcePanel').hide();}
        var selected=row?row.doctorId:$('#CalendarDoctor').val();fill('#AppointmentDoctor',doctors,waitingMode?l('Calendar:AnyDoctor'):null);$('#AppointmentDoctor').prop('required',!waitingMode).val(waitingMode?'':selected||doctors[0]?.id);
        fill('#AppointmentChair',chairs,l('Schedule:NoChair'));$('#AppointmentChair').val(row?.chairId||'');
        var start=row?parts(row.startsAt):{date:calendarDate(),time:'09:00'};
        $('#AppointmentStart').val(start.date+'T'+start.time);
        $('#AppointmentEnd').val(row?parts(row.endsAt).date+'T'+parts(row.endsAt).time:start.date+'T09:30');
        if(column){var duration=row?Math.round((Date.parse(row.endsAt)-Date.parse(row.startsAt))/60000):30;
            $('#AppointmentStart').val(wall(column.date,minute));$('#AppointmentEnd').val(wall(column.date,minute+duration));
            if($('#CalendarView').val()==='chairs')$('#AppointmentChair').val(column.id);else $('#AppointmentDoctor').val(column.id);}
        if(row){
            if(row.status===2||row.status===3||row.status===4) $('#StatusActions').append($('<button type="button" class="btn btn-primary">').text(l('Finance:Visit')).on('click',function(){dental.finance.visit.getByAppointment(row.id).then(function(v){if(v)window.location.href='/Visits/Detail?id='+v.id;else abp.message.info(l('Finance:VisitsEmpty'));});}));
            $('#AppointmentInfo').text(statusName(row.status)+' · '+row.services.map(function(s){return s.name;}).join(', ')+' · '+(row.comment||''));
            if(manage){var targets={0:[1,2,5,6],1:[2,5,6],2:[3,4],3:[4],6:[2]}[row.status]||[];
                targets.forEach(function(s){$('#StatusActions').append($('<button type="button" class="btn btn-outline-primary">').text(statusName(s)).on('click',function(){changeStatus(s);}));});
                $('#StatusActions').append($('<button type="button" class="btn btn-outline-secondary">').text(l('Calendar:Remind')).on('click',function(){api.remind(row.id).then(function(sent){abp.notify.info(l(sent?'Calendar:ReminderLogged':'Calendar:ReminderSkipped'));});}));
            }
        }else {$('#AppointmentComment').val('');$('#AppointmentServices').val([]);searchPatients('');}
        $('#AppointmentForm select,#AppointmentStart,#AppointmentEnd').prop('disabled',!manage);modal.show();
    }
    function changeStatus(status){
        var reason=status===5?$('#CancelReason').val():null;
        if(status===5&&!reason){abp.message.warn(l('Dental:AppointmentReasonRequired'));return;}
        abp.message.confirm(l('Calendar:ConfirmStatus',statusName(status))).then(function(yes){if(!yes||saving)return;saving=true;
            api.changeStatus(current.id,{concurrencyStamp:current.concurrencyStamp,status:status,reasonId:reason,comment:$('#CancelComment').val()}).then(function(){modal.hide();load();}).always(function(){saving=false;});});
    }
    $('#AppointmentForm').on('submit',function(e){e.preventDefault();if(!manage||saving)return;
        if(!current&&!$('#AppointmentPatient').val()){abp.message.warn(l('Calendar:ChoosePatient'));return;}
        if(waitingMode){saving=true;$('#SaveAppointment').prop('disabled',true);
            api.createWaitlist({branchId:branch(),patientId:$('#AppointmentPatient').val(),doctorId:$('#AppointmentDoctor').val()||null,
                preferredFrom:$('#WaitlistFrom').val()?dentalCalendarDates.normalize($('#WaitlistFrom').val()):null,preferredTo:$('#WaitlistTo').val()?dentalCalendarDates.normalize($('#WaitlistTo').val()):null,comment:$('#AppointmentComment').val()})
                .then(function(){modal.hide();load();}).always(function(){saving=false;$('#SaveAppointment').prop('disabled',false);});return;}
        var input={doctorId:$('#AppointmentDoctor').val(),chairId:$('#AppointmentChair').val()||null,startsAt:instant($('#AppointmentStart').val()),endsAt:instant($('#AppointmentEnd').val()),force:$('#AppointmentForce').is(':checked')};
        if(Date.parse(input.endsAt)<=Date.parse(input.startsAt)){abp.message.warn(l('Dental:ScheduleInvalidRange'));return;}
        var request;
        saving=true;$('#SaveAppointment').prop('disabled',true);
        if(current){input.concurrencyStamp=current.concurrencyStamp;input.reasonId=$('#MoveReason').val()||null;request=api.move(current.id,input);}
        else {input.branchId=branch();input.patientId=$('#AppointmentPatient').val();input.comment=$('#AppointmentComment').val();input.services=($('#AppointmentServices').val()||[]).map(function(id){return {serviceId:id,qty:1};});request=api.create(input);}
        request.then(function(){modal.hide();load();}).always(function(){saving=false;$('#SaveAppointment').prop('disabled',false);});
    });
    function searchPatients(filter){var n=++patientGeneration;dental.patients.patient.getLookup(filter,30).then(function(r){if(n!==patientGeneration)return;fill('#AppointmentPatient',r.items);});}
    var searchTimer;$('#PatientSearch').on('input',function(){clearTimeout(searchTimer);var v=this.value;searchTimer=setTimeout(function(){searchPatients(v);},250);});
    $('#QuickPatient').on('click',function(){modal.hide();patientForm.open(null,function(p){fill('#AppointmentPatient',[p]);modal.show();});});
    $('#AppointmentServices').on('change',function(){var selected=$(this).val()||[],duration=selected.reduce(function(sum,id){return sum+(serviceRows.find(function(s){return s.id===id;})?.durationMin||0);},0)||30;
        var value=new Date(Date.parse($('#AppointmentStart').val()+'Z')+duration*60000);$('#AppointmentEnd').val(value.toISOString().slice(0,16));});
    $('#FindSlots').on('click',function(){var duration=Math.round((Date.parse($('#AppointmentEnd').val()+'Z')-Date.parse($('#AppointmentStart').val()+'Z'))/60000);
        api.getAvailableSlots({branchId:branch(),doctorId:$('#AppointmentDoctor').val(),chairId:$('#AppointmentChair').val()||null,date:$('#AppointmentStart').val().slice(0,10),durationMinutes:duration}).then(function(r){
            $('#AvailableSlots').empty();if(!r.items.length)$('#AvailableSlots').text(l('Calendar:NoSlots'));r.items.slice(0,100).forEach(function(s){$('#AvailableSlots').append($('<button type="button" class="btn btn-sm btn-outline-secondary">').text(parts(s.startsAt).time).on('click',function(){$('#AppointmentStart').val(parts(s.startsAt).date+'T'+parts(s.startsAt).time);$('#AppointmentEnd').val(parts(s.endsAt).date+'T'+parts(s.endsAt).time);}));});
        });});
    function renderWaitlist(rows){var root=$('#WaitlistRows').empty();if(!rows.length)root.text(l('Calendar:WaitlistEmpty'));
        rows.forEach(function(w){var line=$('<div class="border rounded p-2 mb-2 d-flex gap-2 flex-wrap">').append($('<span>').text(w.patientName+' · '+(w.preferredFrom||'')+' — '+(w.preferredTo||'')+' '+(w.comment||'')));
            line.append($('<button class="btn btn-sm btn-outline-primary">').text(l('Calendar:Book')).on('click',function(){open(null);fill('#AppointmentPatient',[{id:w.patientId,name:w.patientName}]);if(w.doctorId)$('#AppointmentDoctor').val(w.doctorId);}));
            [1,2,3].forEach(function(status){line.append($('<button class="btn btn-sm btn-outline-secondary">').text(l('Calendar:WaitlistStatus:'+status)).on('click',function(){api.changeWaitlistStatus(w.id,{concurrencyStamp:w.concurrencyStamp,status:status}).then(load);}));});root.append(line);
        });}
    $('#NewWaitlist').on('click',function(){open(null,null,null,true);});
    $('#NewAppointment').toggle(manage).on('click',function(){open(null);});$('#WaitlistPanel').toggle(manage);
    $('#CalendarDate').val(parts(new Date()).date);$('#CalendarDate,#CalendarDoctor').on('change',load);$('#CalendarRefresh').on('click',load);
    $('#CalendarView').on('change',function(){doctorFilterOptions();load();});
    $('#CalendarPrev,#CalendarNext').on('click',function(){var step=$('#CalendarView').val()==='week'?7:1;$('#CalendarDate').val(addDays(calendarDate(),this.id==='CalendarPrev'?-step:step));load();});
    $('#CalendarToday').on('click',function(){$('#CalendarDate').val(parts(new Date()).date);load();});
    $(document).on('keydown',function(e){if(e.ctrlKey||e.metaKey||e.altKey||$(e.target).is('input,select,textarea,[contenteditable]')||$('.modal.show').length)return;var key=e.key.toLowerCase();if(key==='t'){e.preventDefault();$('#CalendarToday').trigger('click');}if(key==='n'&&manage){e.preventDefault();$('#NewAppointment').trigger('click');}if(key==='arrowleft'||key==='arrowright'){e.preventDefault();$(key==='arrowleft'?'#CalendarPrev':'#CalendarNext').trigger('click');}});
    $('#ShowCancelled').on('change',function(){if(data)render();});
    $('#CalendarBranch').on('change',function(){var n=++generation;$.when(dental.staff.employee.getLookup({branchId:branch(),position:3}),dental.branches.branch.getChairs(branch()),dental.staff.employee.getCurrent()).then(function(d,c,me){
        if(n!==generation)return;doctors=d.items;if(!manage&&!abp.auth.isGranted('Dental.Schedule.ViewAll'))doctors=doctors.filter(function(x){return x.id===me.employeeId;});
        chairs=c.items.filter(function(x){return x.isActive;});doctorFilterOptions();load();});});
    dental.branches.branch.getLookup().then(function(r){fill('#CalendarBranch',r.items);var appointmentId=new URLSearchParams(window.location.search).get('appointmentId');
        if(appointmentId){api.get(appointmentId).then(function(a){linkedAppointment=a;$('#CalendarBranch').val(a.branchId);$('#CalendarDate').val(parts(a.startsAt).date);$('#CalendarBranch').trigger('change');});}
        else {var query=new URLSearchParams(window.location.search),requestedBranch=query.get('branchId'),requestedDate=query.get('date');if(requestedBranch&&r.items.some(function(b){return b.id===requestedBranch;}))$('#CalendarBranch').val(requestedBranch);if(requestedDate){try{$('#CalendarDate').val(dentalCalendarDates.normalize(requestedDate));}catch{}}$('#CalendarBranch').trigger('change');}});
    if(manage){dental.catalog.catalog.getServices({}).then(function(r){serviceRows=r.items.filter(function(s){return s.isActive;});fill('#AppointmentServices',serviceRows);});
        dental.references.cancelReason.getList(0).then(function(r){fill('#CancelReason',r.items,l('Calendar:SelectReason'));});
        dental.references.cancelReason.getList(1).then(function(r){fill('#MoveReason',r.items,l('Calendar:OptionalReason'));});}
});
