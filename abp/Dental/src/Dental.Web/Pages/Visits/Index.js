$(function(){
    var api=dental.finance.visit,l=abp.localization.getResource('Dental');
    function load(){api.getList({branchId:$('#VisitBranch').val()}).then(function(rows){var root=$('#VisitRows').empty();$('#VisitsEmpty').toggleClass('d-none',!!rows.length);rows.forEach(function(v){
        root.append($('<tr>').append($('<td>').text(dentalUi.dateTime(v.openedAt)),$('<td>').text(v.patientName),$('<td>').text(v.doctorName),$('<td>').text(l('Finance:VisitStatus:'+v.status)),$('<td>').text(dentalUi.money(v.total)),$('<td>').text(dentalUi.money(v.debt)),$('<td>').append($('<a class="btn btn-sm btn-outline-primary">').attr('href','/Visits/Detail?id='+v.id).text(l('Finance:Open')))));});});}
    dental.branches.branch.getLookup().then(function(r){r.items.forEach(function(b){$('#VisitBranch').append($('<option>').val(b.id).text(b.name));});load();});$('#VisitBranch').on('change',load);
});
