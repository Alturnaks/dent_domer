var dentalCalendarLayout = (function () {
    var pixelsPerMinute = 2;
    function position(from, to, gridStart, gridEnd) {
        from = Math.max(from, gridStart); to = Math.min(to, gridEnd);
        return to > from ? {top: (from - gridStart) * pixelsPerMinute, height: (to - from) * pixelsPerMinute} : null;
    }
    function minuteAt(offset, gridStart, gridEnd, step) {
        return Math.min(gridEnd - step, Math.max(gridStart, Math.floor((offset / pixelsPerMinute + gridStart) / step) * step));
    }
    function resizeEnd(offset, gridStart, gridEnd, appointmentStart, step) {
        return Math.min(gridEnd,Math.max(appointmentStart+step,Math.round((offset/pixelsPerMinute+gridStart)/step)*step));
    }
    // Pack overlapping visible records (including cancelled records) into separate lanes.
    function lanes(entries) {
        var sorted = entries.slice().sort(function(a,b){return a.start-b.start || a.end-b.end || String(a.id).localeCompare(String(b.id));});
        var result = {}, group = [], groupEnd = -Infinity;
        function flush() {
            var ends = [], placed = [];
            group.forEach(function(entry){
                var lane = ends.findIndex(function(end){return end <= entry.start;});
                if(lane < 0) lane = ends.length;
                ends[lane] = entry.end; placed.push({entry:entry,lane:lane});
            });
            placed.forEach(function(p){result[p.entry.id]={lane:p.lane,count:ends.length};});
            group = []; groupEnd = -Infinity;
        }
        sorted.forEach(function(entry){
            if(group.length && entry.start >= groupEnd) flush();
            group.push(entry); groupEnd = Math.max(groupEnd,entry.end);
        });
        flush(); return result;
    }
    return {pixelsPerMinute:pixelsPerMinute,position:position,minuteAt:minuteAt,resizeEnd:resizeEnd,lanes:lanes};
})();
