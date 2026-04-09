using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Domain.Entities;

namespace AciModule.Domain.Entitys
{
    /// <summary>
    /// 交管任务实体
    /// </summary>
    public class TrafficControlTask : Entity<Guid>
    {
        /// <summary>
        /// 区域ID
        /// </summary>
        public int RegionId { get; protected set; }

        /// <summary>
        /// AGV编号
        /// </summary>
        public string AgvNo { get; protected set; }

        /// <summary>
        /// 任务类型：1-进入区域，2-离开区域
        /// </summary>
        public int TaskType { get; protected set; }

        /// <summary>
        /// 任务状态：0-待处理，1-处理中，2-已完成，3-失败
        /// </summary>
        public int Status { get; protected set; }

        /// <summary>
        /// 请求时间
        /// </summary>
        public DateTime RequestTime { get; protected set; }

        /// <summary>
        /// 处理时间
        /// </summary>
        public DateTime? ProcessTime { get; protected set; }

        /// <summary>
        /// 完成时间
        /// </summary>
        public DateTime? CompleteTime { get; protected set; }

        /// <summary>
        /// 请求URL
        /// </summary>
        public string RequestUrl { get; protected set; }

        /// <summary>
        /// 响应结果
        /// </summary>
        public string Response { get; protected set; }

        /// <summary>
        /// 失败原因
        /// </summary>
        public string FailReason { get; protected set; }

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; protected set; }

        /// <summary>
        /// 订单索引
        /// </summary>
        public int OrderIndex { get; protected set; }

        protected TrafficControlTask()
        {
        }

        public TrafficControlTask(
            Guid id,
            int regionId,
            string agvNo,
            int taskType,
            int orderIndex)
        {
            Id = id;
            RegionId = regionId;
            AgvNo = agvNo;
            TaskType = taskType;
            Status = 0; // 待处理
            RequestTime = DateTime.Now;
            RetryCount = 0;
            OrderIndex = orderIndex;
        }

        /// <summary>
        /// 设置处理中状态
        /// </summary>
        /// <param name="requestUrl">请求URL</param>
        public void SetProcessing(string requestUrl)
        {
            Status = 1;
            ProcessTime = DateTime.Now;
            RequestUrl = requestUrl;
        }

        /// <summary>
        /// 设置完成状态
        /// </summary>
        /// <param name="response">响应结果</param>
        public void SetCompleted(string response)
        {
            Status = 2;
            CompleteTime = DateTime.Now;
            Response = response;
        }

        /// <summary>
        /// 设置失败状态
        /// </summary>
        /// <param name="failReason">失败原因</param>
        public void SetFailed(string failReason)
        {
            Status = 3;
            CompleteTime = DateTime.Now;
            FailReason = failReason;
            RetryCount++;
        }

        /// <summary>
        /// 重置为待处理状态
        /// </summary>
        public void ResetToWaiting()
        {
            Status = 0;
            ProcessTime = null;
        }
    }
} 