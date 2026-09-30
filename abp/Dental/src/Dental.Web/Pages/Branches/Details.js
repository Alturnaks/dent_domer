$(function () {
    var l = abp.localization.getResource('Dental');
    var service = dental.branches.branch;
    var branchId = $('#BranchDetails').data('branch-id');
    var rooms = [];

    function esc(s) { return $('<div>').text(s || '').html(); }

    function roomName(id) {
        var r = rooms.find(function (x) { return x.id === id; });
        return r ? r.name : '—';
    }

    function loadRooms() {
        return service.getRooms(branchId).then(function (res) {
            rooms = res.items;
            var $tb = $('#RoomsTable tbody').empty();
            var $sel = $('#NewChairRoom').empty().append($('<option>').val('').text(l('Branch:NoRoom')));
            rooms.forEach(function (r) {
                $sel.append($('<option>').val(r.id).text(r.name));
                $tb.append('<tr><td>' + esc(r.name) + '</td><td class="text-end">' +
                    '<button class="btn btn-sm btn-outline-secondary me-1" data-rename-room="' + r.id + '"><i class="fa fa-pen"></i></button>' +
                    '<button class="btn btn-sm btn-outline-danger" data-delete-room="' + r.id + '"><i class="fa fa-trash"></i></button></td></tr>');
            });
        });
    }

    function loadChairs() {
        return service.getChairs(branchId).then(function (res) {
            var $tb = $('#ChairsTable tbody').empty();
            res.items.forEach(function (c) {
                $tb.append('<tr><td>' + esc(c.name) + '</td><td class="text-muted">' + esc(roomName(c.roomId)) + '</td><td>' +
                    (c.isActive ? '' : '<span class="badge bg-secondary">' + l('Inactive') + '</span>') + '</td><td class="text-end">' +
                    '<button class="btn btn-sm btn-outline-secondary me-1" data-toggle-chair="' + c.id + '" data-name="' + esc(c.name) + '" data-room="' + (c.roomId || '') + '" data-active="' + c.isActive + '"><i class="fa fa-power-off"></i></button>' +
                    '<button class="btn btn-sm btn-outline-danger" data-delete-chair="' + c.id + '"><i class="fa fa-trash"></i></button></td></tr>');
            });
        });
    }

    function reload() { loadRooms().then(loadChairs); }

    $('#AddRoomButton').click(function () {
        var name = $('#NewRoomName').val();
        if (!name) { return; }
        service.createRoom(branchId, { name: name }).then(function () { $('#NewRoomName').val(''); reload(); });
    });

    $('#AddChairButton').click(function () {
        var name = $('#NewChairName').val();
        if (!name) { return; }
        service.createChair(branchId, { name: name, roomId: $('#NewChairRoom').val() || null, isActive: true })
            .then(function () { $('#NewChairName').val(''); loadChairs(); });
    });

    $(document).on('click', '[data-rename-room]', function () {
        var id = $(this).data('rename-room');
        var name = window.prompt(l('Branch:RoomName'), roomName(id));
        if (name) { service.updateRoom(id, { name: name }).then(reload); }
    });

    $(document).on('click', '[data-delete-room]', function () {
        var id = $(this).data('delete-room');
        abp.message.confirm(l('AreYouSure')).then(function (ok) { if (ok) { service.deleteRoom(id).then(reload); } });
    });

    $(document).on('click', '[data-toggle-chair]', function () {
        var $b = $(this);
        service.updateChair($b.data('toggle-chair'), {
            name: $b.data('name'), roomId: $b.data('room') || null, isActive: !($b.data('active') === true || $b.data('active') === 'true')
        }).then(loadChairs);
    });

    $(document).on('click', '[data-delete-chair]', function () {
        var id = $(this).data('delete-chair');
        abp.message.confirm(l('AreYouSure')).then(function (ok) { if (ok) { service.deleteChair(id).then(loadChairs); } });
    });

    reload();
});
