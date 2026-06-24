using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts
{
    public interface ITag
    {
        string Name { get; set; }
        string Description { get; set; }
        string Address { get; set; }
        Tag_Type Type { get; set; }
    }
}
