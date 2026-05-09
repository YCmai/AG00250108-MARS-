
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Client100.Entity
{

    public class Read110PLCDbAddress
    {
      /// <summary>
      /// PLC地址
      /// </summary>
        public string DBAddress { get; set; }
        /// <summary>
        /// 备注
        /// </summary>
        public string Remark { get; set; }
        /// <summary>
        /// DB类型
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        /// IP地址
        /// </summary>
        public string IP { get; set; }
        /// <summary>
        /// DB块中文备注
        /// </summary>
        public string AddressRemark { get; set; }
        /// <summary>
        /// DB地址类型
        /// </summary>
        public string DBType { get; set; }


        /// <summary>
        /// PLC管理的方法，方便对应是那个PLC方法管理
        /// </summary>
        public string ManagerType { get; set; }


        public string Value { get; set; }

        public DateTime? UpdateTime { get; set; }
    }

}
