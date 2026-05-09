using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Client100.Entity
{
    public class OP110Socket
    {
        public int ID { get; set; }
        /// <summary>
        /// 任务编号
        /// </summary>
        public string OrderCode { get; set; }
        /// <summary>
        /// 0读取bool值，1写入bool值，2重置bool值，3读取和写入成功，4读取INT值，5，读取的INT值为NG，10写INT值
        /// </summary>
        public int Status { get; set; }
        /// <summary>
        /// 是否发送
        /// </summary>
        public int IsSend { get; set; }
        /// <summary>
        /// 信号信息例如请求进入，小车到位
        /// </summary>
        public string Signal { get; set; }


        public DateTime? CreatingTime { get; set; }

        /// <summary>
        /// 备注描述
        /// </summary>
        public string Remark { get; set; }


        public DateTime? UpdateTime { get; set; }

        /// <summary>
        /// PLC类型，用来查找对应的地址位
        /// </summary>
        public string PlcType { get; set; }
    }
}