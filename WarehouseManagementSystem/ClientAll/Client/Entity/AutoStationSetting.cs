using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Service
{
    public class AutoStationSetting
    {
        /// <summary>
        /// 工位
        /// </summary>
        public string Node { get; set; }

        /// <summary>
        /// 工位备注
        /// </summary>
        public string NodeRemark { get; set; }


        /// <summary>
        /// 工位排序，任务用
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 信号状态，用来区分是否上位机需要是否存在叫车信号
        /// </summary>
        public string SignalStatus { get; set; }


        /// <summary>
        /// 对应地图的SYNCID
        /// </summary>
        public int SyncId { get; set; }

        /// <summary>
        /// 进去等待点
        /// </summary>
        public string GoInWattingNode { get; set; }

        /// <summary>
        /// 出来等待点
        /// </summary>
        public string ComeOutWattingNode { get; set; }



        /// <summary>
        /// 是否平行工位，是，否
        /// </summary>
        public string ParallelOrNotNode { get; set; }



        /// <summary>
        /// 有车，无车
        /// </summary>
        public string IfCar { get; set; }



        /// <summary>
        /// 工位类型，人工，自动
        /// </summary>
        public string NodeType { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string PlcType { get; set; }

    }
}
