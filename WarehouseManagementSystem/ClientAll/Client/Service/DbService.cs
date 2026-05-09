using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Dapper;

using Client100.Entity;
using Service;

namespace Client100.Service
{
    public class DbService
    {

        string connectionString = ConfigurationManager.ConnectionStrings["conn"].ToString();
        public static LoggerHelper log = new LoggerHelper();



        public RCS_QrCodes GetRCS_QrCodes(RCS_QrCodes model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<RCS_QrCodes>("select * from RCS_QrCodes where CarIP=@CarIP and Excute =@Excute", model).FirstOrDefault();
                }
            }
            catch (Exception)
            {

                throw;
            }

        }

        /// <summary>
        /// 插入
        /// </summary>
        /// <returns></returns>
        public int InsertRCS_QrCodes(RCS_QrCodes model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("insert into RCS_QrCodes(QRCode,CreateTime,CarIP,TaskType,Normal,IfSend,Remark,Excute) values(@QRCode,@CreateTime,@CarIP,@TaskType,@Normal,@IfSend,@Remark,@Excute)", model);
            }
        }


        /// <summary>
        /// 插入
        /// </summary>
        /// <returns></returns>
        public int Insert(OP100Socket model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("insert into UserTask(DeviceId,DemandCode,PlaceCode,TaskId,TaskStatus,CreatDateTime,Executed,NodeType,OrderCode) values(@DeviceId,@DemandCode,@PlaceCode,@TaskId,@TaskStatus,@CreatDateTime,@Executed,@NodeType,@OrderCode)", model);
            }
        }


        public AutoStationSetting GetAutoStationSettingByNode(AutoStationSetting model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<AutoStationSetting>("select * from AutoStationSettings where Node=@Node", model).FirstOrDefault();
                }
            }
            catch (Exception)
            {

                throw;
            }

        }

        public int UpdateAutoStationSetting(AutoStationSetting model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Execute("update AutoStationSettings set SignalStatus = @SignalStatus where  Sort =@Sort", model);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }



        public AutoStationSetting GetAutoStationSettingBySort(AutoStationSetting model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<AutoStationSetting>("select * from AutoStationSettings where Sort=@Sort", model).FirstOrDefault();
                }
            }
            catch (Exception)
            {

                throw;
            }

        }


        public int UpdateAutoStationSettingByNode(AutoStationSetting model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Execute("update AutoStationSettings set SignalStatus = @SignalStatus where  Node =@Node", model);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }



        public int AandMovementMarkLogsInsert(AandMovementMarkLogs model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Execute("insert into AandMovementMarkLogs(Content,CreateDateTime) values(@Content,@CreateDateTime)", model);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }





        public OP100Socket TaskQuery(OP100Socket model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP100Socket>("select * from UserTask where DemandCode=@DemandCode  and (TaskStatus= 1 or TaskStatus = 2)", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }


        public PLCDbAddress QueryPLCDbAddressByOP(PLCDbAddress model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<PLCDbAddress>("select * from PLCDbAddress where AddressRemark=@AddressRemark  and Type=@Type", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

       

        public List<OP100Socket> GetNoSendPlcTask()
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP100Socket>("select OrderCode from OP100Sockets where IsSend=0  GROUP BY OrderCode ").ToList();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public OP100Socket GetNoSendPlcTaskOrderByTime(OP100Socket model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP100Socket>("select * from OP100Sockets where OrderCode=@OrderCode and IsSend=0 Order by  CreatingTime asc ", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public OP110Socket GetNoSendPlcTaskOrder110ByTime(OP110Socket model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP110Socket>("select * from OP110Sockets where OrderCode=@OrderCode and IsSend=0 Order by  CreatingTime asc ", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public OP120Socket GetNoSendPlcTaskOrder120ByTime(OP120Socket model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP120Socket>("select * from OP120Sockets where OrderCode=@OrderCode and IsSend=0 Order by  CreatingTime asc ", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }


        public List<Read100PLCDbAddress> Get100AllPlcTask()
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<Read100PLCDbAddress>("select * from Read100PLCDbAddress").ToList();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public List<Read110PLCDbAddress> Get110AllPlcTask()
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<Read110PLCDbAddress>("select * from Read110PLCDbAddress").ToList();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public List<Read120PLCDbAddress> Get120AllPlcTask()
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<Read120PLCDbAddress>("select * from Read120PLCDbAddress").ToList();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public List<OP110Socket> Get110NoSendPlcTask()
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP110Socket>("select OrderCode from OP110Sockets where IsSend=0  GROUP BY OrderCode").ToList();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public List<OP120Socket> Get120NoSendPlcTask()
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP120Socket>("select OrderCode from OP120Sockets where IsSend=0  GROUP BY OrderCode").ToList();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }



        public Read100PLCDbAddress GetRead100PLCDbAddressBySignal(Read100PLCDbAddress model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<Read100PLCDbAddress>("select * from Read100PLCDbAddress where AddressRemark=@AddressRemark", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public Read110PLCDbAddress GetRead110PLCDbAddressBySignal(Read110PLCDbAddress model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<Read110PLCDbAddress>("select * from Read110PLCDbAddress where AddressRemark=@AddressRemark", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }


        public Read120PLCDbAddress GetRead120PLCDbAddressBySignal(Read120PLCDbAddress model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<Read120PLCDbAddress>("select * from Read120PLCDbAddress where AddressRemark=@AddressRemark", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }


        public OP100Socket GetPlcTaskBySignal(OP100Socket model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP100Socket>("select * from OP100Sockets where Signal=@Signal and Status=@Status and IsSend=0  order by CreatingTime ASC", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public OP110Socket Get110PlcTaskBySignal(OP110Socket model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP110Socket>("select * from OP110Sockets where Signal=@Signal and Status=@Status and IsSend=0  order by CreatingTime ASC", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public OP120Socket Get120PlcTaskBySignal(OP120Socket model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<OP120Socket>("select * from OP120Sockets where Signal=@Signal and Status=@Status and IsSend=0  order by CreatingTime ASC", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }

        public UserTask GetUserTaskByTaskCode(UserTask model)
        {
            try
            {
                using (IDbConnection connection = new SqlConnection(connectionString))
                {
                    return connection.Query<UserTask>("select * from UserTasks where TaskCode =@TaskCode", model).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {

                log.Error(ex.Message);
                return null;
            }
        }



        public int InsertSmAutoPlcTask(SmAutoPlcTask model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("insert into SmAutoPlcTasks(OrderCode,Status,IsSend,Signal,CreatingTime,Remark,PlcType) values(@OrderCode,@Status,@IsSend,@Signal,@CreatingTime,@Remark,@PlcType)", model);
            }
        }

        public int InsertOP100Socket(OP100Socket model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("insert into OP100Sockets(OrderCode,Status,IsSend,Signal,CreatingTime,Remark,PlcType) values(@OrderCode,@Status,@IsSend,@Signal,@CreatingTime,@Remark,@PlcType)", model);
            }
        }

        public int InsertOP110Socket(OP110Socket model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("insert into OP110Sockets(OrderCode,Status,IsSend,Signal,CreatingTime,Remark,PlcType) values(@OrderCode,@Status,@IsSend,@Signal,@CreatingTime,@Remark,@PlcType)", model);
            }
        }


        public int InsertOP120Socket(OP120Socket model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("insert into OP120Sockets(OrderCode,Status,IsSend,Signal,CreatingTime,Remark,PlcType) values(@OrderCode,@Status,@IsSend,@Signal,@CreatingTime,@Remark,@PlcType)", model);
            }
        }


        public int InsertAutoPlcTask(AutoPlcTask model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("insert into AutoPlcTasks(OrderCode,Status,IsSend,Signal,CreatingTime,Remark,PlcType) values(@OrderCode,@Status,@IsSend,@Signal,@CreatingTime,@Remark,@PlcType)", model);
            }
        }

        public int UpdateOP100Socket(OP100Socket model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("update OP100Sockets set IsSend=@IsSend ,Status=@Status,UpdateTime=@UpdateTime where ID=@ID", model);
            }
        }

        public int UpdateRead100PLCDbAddress(Read100PLCDbAddress model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("Update Read100PLCDbAddress set Value =@Value,UpdateTime=@UpdateTime where DBAddress=@DBAddress and Type=@Type and AddressRemark=@AddressRemark", model);
            }
        }

        public int UpdateRead110PLCDbAddress(Read110PLCDbAddress model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("Update Read110PLCDbAddress set Value =@Value,UpdateTime=@UpdateTime where DBAddress=@DBAddress and Type=@Type and AddressRemark=@AddressRemark", model);
            }
        }

        public int UpdateRead120PLCDbAddress(Read120PLCDbAddress model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("Update Read120PLCDbAddress set Value =@Value,UpdateTime=@UpdateTime where DBAddress=@DBAddress and Type=@Type and AddressRemark=@AddressRemark", model);
            }
        }

        public int UpdateOP110Socket(OP110Socket model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("update OP110Sockets set IsSend=@IsSend ,Status=@Status,UpdateTime=@UpdateTime where ID=@ID", model);
            }
        }

        public int UpdateOP120Socket(OP120Socket model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("update OP120Sockets set IsSend=@IsSend ,Status=@Status,UpdateTime=@UpdateTime where ID=@ID", model);
            }
        }


        public int UpdateUserTaskByOrderCode(UserTask model)
        {
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.Execute("update UserTasks set WStatus =@WStatus where TaskCode=@TaskCode", model);
            }
        }
    }
}
