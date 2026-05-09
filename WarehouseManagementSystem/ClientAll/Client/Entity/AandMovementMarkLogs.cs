using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Service
{
    public class AandMovementMarkLogs
    {
        public string Content { get; set; }

        public DateTime CreateDateTime { get; set; }

        /// <summary>
        /// 0初始化，1等待发送，2发送完成
        /// </summary>
        public int ISSendOKAGV { get; set; }
    }


}