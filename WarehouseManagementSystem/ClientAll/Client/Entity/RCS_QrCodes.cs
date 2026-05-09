using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Client100.Entity
{
    public class RCS_QrCodes
    {

        public int Id { get; set; }
        /// <summary>
        /// 物料编码
        /// </summary>
        public string QRCode { get; set; }

        /// <summary>
        /// 生产日期-日期
        /// </summary>
        public DateTime? CreateTime { get; set; }


        public TaskType TaskType { get; set; }

        /// <summary>
        /// 对应的任务ID
        /// </summary>
        public string CarIP { get; set; }

        /// <summary>
        /// 是否请求到MT系统
        /// </summary>
        public bool IfSend { get; set; }

        /// <summary>
        /// 请求MT系统反馈数据是否异常
        /// </summary>
        public bool Normal { get; set; }


        public string Remark { get; set; }

        public bool Excute { get; set; }
    }

    public enum TaskType
    {
        /// <summary>
        /// 满LOAD入库
        /// </summary>
        fullload,
        /// <summary>
        /// 桶装胶芯入库
        /// </summary>
        tzjxrk,
        /// <summary>
        /// 废料搬送
        /// </summary>
        flbs,
        /// <summary>
        /// Coating平台物料互调
        /// </summary>
        coatingptwlhd,
        /// <summary>
        /// 空托盘回收
        /// </summary>
        ktphs,
        /// <summary>
        /// 异常胶粒处理
        /// </summary>
        ycjlcl,

        /// <summary>
        /// 桶装胶芯出库
        /// </summary>
        tzjxck,

        /// <summary>
        /// 呼叫盒叫料
        /// </summary>
        hjhjl,


        /// <summary>
        /// 呼叫盒异常物料退料
        /// </summary>
        hjhyctl,

        /// <summary>
        /// 呼叫盒空托盘
        /// </summary>
        hjhktp


    }
}