using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataConcentrator
{
    public class ContextClass : DbContext
    {
        //singleton pattern
        private static ContextClass instance;
        private static readonly object syncRoot = new object();

        public static object SyncRoot => syncRoot;

        public static ContextClass Instance
        {
            get
            {
                lock (syncRoot)
                {
                    if (instance == null)
                    {
                        instance = new ContextClass();
                    }

                    return instance;
                }
            }
        }

        public DbSet<Tag> Tags { get; set; }
        public DbSet<AlarmRecord> AlarmRecords { get; set; }

    }
}
