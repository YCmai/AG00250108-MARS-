using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Client100.Entity
{
    public class UserTask
    {

        public int DeviceId { get; set; }
        /// <summary>
        /// 取货点
        /// </summary>
        public string PickUpPoint { get; set; }


        public string UnloadingPoint { get; set; }

        /// <summary>
        /// 用户查询绑定的任务ID
        /// </summary>
        public int TaskId { get; set; }

        /// <summary>
        /// 任务状态
        /// </summary>
        public EAppTaskStatus TaskStatus { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? ExecutedTime { get; set; }


        /// <summary>
        /// 设备管理器任务ID
        /// </summary>
        public string RunTaskId { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 是否要执行
        /// </summary>
        public bool Executed { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }


        /// <summary>
        /// 任务编号
        /// </summary>
        public string TaskCode { get; set; }

        /// <summary>
        /// 0手动工位第一个任务，1手动工位不用交互，2自动工位要交互,3NG工位，4自动工位平行工位，5人工平行工位任务,6自动工位到手动工位过程，7手动到自动，8，手动到手动
        /// </summary>
        public int TaskType { get; set; }

        /// <summary>
        /// 排序
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 物料状态
        /// </summary>
        public string WStatus { get; set; }
    }

    public enum EAppTaskStatus
    {
        Waitting = 0x01 << 0,    //等待执行1
        Working = 0x01 << 1,    //正在执行2
        Finished = 0x01 << 2,   //已经完成4
        Cancel = 0x01 << 3,     //取消8
        ExcuteError = 0x01 << 4,   //参数错误
    }
}