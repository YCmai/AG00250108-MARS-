using System.Collections.Generic;

namespace WarehouseManagementSystem.Models.Common
{
    /// <summary>
    /// 通用操作结果类
    /// </summary>
    public class Result
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Succeeded { get; set; }
        
        /// <summary>
        /// 错误消息
        /// </summary>
        public string Message { get; set; }
        
        /// <summary>
        /// 返回数据
        /// </summary>
        public object Data { get; set; }
        
        /// <summary>
        /// 错误列表
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 创建成功结果
        /// </summary>
        /// <param name="message">成功消息</param>
        /// <returns>结果对象</returns>
        public static Result Success(string message = "操作成功")
        {
            return new Result { Succeeded = true, Message = message };
        }

        /// <summary>
        /// 创建带数据的成功结果
        /// </summary>
        /// <param name="data">返回数据</param>
        /// <param name="message">成功消息</param>
        /// <returns>结果对象</returns>
        public static Result Success(object data, string message = "操作成功")
        {
            return new Result { Succeeded = true, Message = message, Data = data };
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>结果对象</returns>
        public static Result Failure(string message)
        {
            return new Result { Succeeded = false, Message = message };
        }

        /// <summary>
        /// 创建带错误列表的失败结果
        /// </summary>
        /// <param name="errors">错误列表</param>
        /// <param name="message">错误消息</param>
        /// <returns>结果对象</returns>
        public static Result Failure(List<string> errors, string message = "操作失败")
        {
            return new Result { Succeeded = false, Message = message, Errors = errors };
        }
    }

    /// <summary>
    /// 泛型操作结果类
    /// </summary>
    /// <typeparam name="T">返回数据类型</typeparam>
    public class Result<T>
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Succeeded { get; set; }
        
        /// <summary>
        /// 错误消息
        /// </summary>
        public string Message { get; set; }
        
        /// <summary>
        /// 返回数据
        /// </summary>
        public T Data { get; set; }
        
        /// <summary>
        /// 错误列表
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 创建成功结果
        /// </summary>
        /// <param name="data">返回数据</param>
        /// <param name="message">成功消息</param>
        /// <returns>结果对象</returns>
        public static Result<T> Success(T data, string message = "操作成功")
        {
            return new Result<T> { Succeeded = true, Message = message, Data = data };
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>结果对象</returns>
        public static Result<T> Failure(string message)
        {
            return new Result<T> { Succeeded = false, Message = message };
        }

        /// <summary>
        /// 创建带错误列表的失败结果
        /// </summary>
        /// <param name="errors">错误列表</param>
        /// <param name="message">错误消息</param>
        /// <returns>结果对象</returns>
        public static Result<T> Failure(List<string> errors, string message = "操作失败")
        {
            return new Result<T> { Succeeded = false, Message = message, Errors = errors };
        }
    }
} 