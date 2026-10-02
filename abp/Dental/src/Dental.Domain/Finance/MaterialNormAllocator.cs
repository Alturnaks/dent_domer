using System;
using System.Collections.Generic;
using System.Linq;

namespace Dental.Finance;

public static class MaterialNormAllocator
{
    // Preserve the historical norm of a service row. Splitting a row must not multiply its norm.
    public static decimal[] Allocate(IEnumerable<(Guid Item,Guid? VisitItem,decimal Norm)> previous,IReadOnlyList<(Guid Item,Guid? VisitItem,decimal Quantity)> next)
    {
        var old=previous.ToList();var result=new decimal[next.Count];
        foreach(var item in next.Select(m=>m.Item).Distinct())
        {
            var indexes=Enumerable.Range(0,next.Count).Where(i=>next[i].Item==item).ToList();
            var oldRows=old.Where(m=>m.Item==item).ToList();var allocated=0m;var remaining=new List<int>();
            foreach(var group in indexes.GroupBy(i=>next[i].VisitItem))
            {
                var matching=oldRows.Where(m=>m.VisitItem!=null&&m.VisitItem==group.Key).ToList();
                if(matching.Count==0){remaining.AddRange(group);continue;}
                var norm=matching.Sum(m=>m.Norm);var quantity=group.Sum(i=>next[i].Quantity);
                foreach(var i in group)result[i]=quantity>0?norm*next[i].Quantity/quantity:0;
                allocated+=norm;
            }
            var remainder=Math.Max(0,oldRows.Sum(m=>m.Norm)-allocated);var total=remaining.Sum(i=>next[i].Quantity);
            foreach(var i in remaining)result[i]=total>0?remainder*next[i].Quantity/total:0;
        }
        return result;
    }
}
