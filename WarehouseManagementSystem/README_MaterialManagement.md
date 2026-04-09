# 物料管理模块使用文档

## 概述

物料管理模块提供仓库中物料的基础信息管理、出入库管理、库存查询等功能，支持物料的入库、出库、库存调整和库内移位等操作，并记录所有物料交易历史，便于追踪和查询。

## 数据库设计

### 物料表 (Materials)

存储物料的基本信息。

| 字段名 | 类型 | 描述 |
| ------ | ---- | ---- |
| Id | INT | 物料ID，主键 |
| Code | NVARCHAR(50) | 物料编码，唯一标识 |
| Name | NVARCHAR(100) | 物料名称 |
| Specification | NVARCHAR(100) | 规格型号 |
| Unit | NVARCHAR(20) | 单位 |
| Quantity | DECIMAL(18, 2) | 当前库存数量 |
| MinStock | DECIMAL(18, 2) | 最小库存 |
| MaxStock | DECIMAL(18, 2) | 最大库存 |
| LocationCode | NVARCHAR(50) | 储位编码 |
| ImageUrl | NVARCHAR(255) | 物料图片URL |
| CreateTime | DATETIME | 创建时间 |
| UpdateTime | DATETIME | 更新时间 |
| Remark | NVARCHAR(500) | 备注信息 |

### 物料交易记录表 (RCS_MaterialTransactions)

记录所有物料的出入库、库存调整和移位等操作记录。

| 字段名 | 类型 | 描述 |
| ------ | ---- | ---- |
| Id | INT | 交易记录ID，主键 |
| TransactionCode | NVARCHAR(50) | 交易单号，唯一标识 |
| MaterialId | INT | 物料ID，外键关联Materials表 |
| MaterialCode | NVARCHAR(50) | 物料编码 |
| Type | INT | 交易类型（1-入库，2-出库，3-调整，4-移位，5-盘点） |
| Quantity | DECIMAL(18, 2) | 交易数量 |
| BeforeQuantity | DECIMAL(18, 2) | 交易前库存 |
| AfterQuantity | DECIMAL(18, 2) | 交易后库存 |
| LocationCode | NVARCHAR(50) | 储位编码 |
| TargetLocationCode | NVARCHAR(50) | 目标储位编码（用于移位操作） |
| BatchNumber | NVARCHAR(50) | 批次号 |
| OperatorId | NVARCHAR(50) | 操作人ID |
| OperatorName | NVARCHAR(50) | 操作人姓名 |
| TaskId | INT | 关联任务ID |
| TaskCode | NVARCHAR(50) | 关联任务编号 |
| Remark | NVARCHAR(500) | 备注信息 |
| CreateTime | DATETIME | 创建时间 |

## API接口说明

### 物料基础信息管理

#### 获取所有物料

```
GET /api/Material
```

返回所有物料的列表。

**返回示例**：

```json
[
  {
    "id": 1,
    "code": "M001",
    "name": "钢管",
    "specification": "φ20mm×1000mm",
    "unit": "根",
    "quantity": 100.00,
    "minStock": 20.00,
    "maxStock": 200.00,
    "locationCode": "A-01-01",
    "imageUrl": null,
    "createTime": "2023-01-01T00:00:00",
    "updateTime": null,
    "remark": "普通钢管"
  },
  // 更多物料...
]
```

#### 根据编码获取物料

```
GET /api/Material/{code}
```

根据物料编码获取物料详细信息。

**参数**：

- `code`：物料编码

**返回示例**：

```json
{
  "id": 1,
  "code": "M001",
  "name": "钢管",
  "specification": "φ20mm×1000mm",
  "unit": "根",
  "quantity": 100.00,
  "minStock": 20.00,
  "maxStock": 200.00,
  "locationCode": "A-01-01",
  "imageUrl": null,
  "createTime": "2023-01-01T00:00:00",
  "updateTime": null,
  "remark": "普通钢管"
}
```

#### 添加物料

```
POST /api/Material
```

添加新物料信息。

**请求参数**：

```json
{
  "code": "M002",
  "name": "螺丝",
  "specification": "M8×30mm",
  "unit": "个",
  "minStock": 100.00,
  "maxStock": 1000.00,
  "locationCode": "A-01-02",
  "remark": "不锈钢螺丝"
}
```

**返回示例**：

```json
{
  "succeeded": true,
  "message": "添加物料成功",
  "data": {
    "id": 2,
    "code": "M002",
    "name": "螺丝",
    "specification": "M8×30mm",
    "unit": "个",
    "quantity": 0.00,
    "minStock": 100.00,
    "maxStock": 1000.00,
    "locationCode": "A-01-02",
    "imageUrl": null,
    "createTime": "2023-05-15T10:30:00",
    "updateTime": null,
    "remark": "不锈钢螺丝"
  },
  "errors": []
}
```

#### 更新物料

```
PUT /api/Material/{id}
```

更新物料信息。

**参数**：

- `id`：物料ID

**请求参数**：

```json
{
  "id": 2,
  "code": "M002",
  "name": "不锈钢螺丝",
  "specification": "M8×30mm",
  "unit": "个",
  "minStock": 200.00,
  "maxStock": 2000.00,
  "locationCode": "A-01-02",
  "remark": "不锈钢螺丝，加固用"
}
```

**返回示例**：

```json
{
  "succeeded": true,
  "message": "更新物料成功",
  "data": {
    "id": 2,
    "code": "M002",
    "name": "不锈钢螺丝",
    "specification": "M8×30mm",
    "unit": "个",
    "quantity": 0.00,
    "minStock": 200.00,
    "maxStock": 2000.00,
    "locationCode": "A-01-02",
    "imageUrl": null,
    "createTime": "2023-05-15T10:30:00",
    "updateTime": "2023-05-15T11:30:00",
    "remark": "不锈钢螺丝，加固用"
  },
  "errors": []
}
```

### 物料出入库管理

#### 物料入库

```
POST /api/Material/instock
```

执行物料入库操作。

**请求参数**：

```json
{
  "materialCode": "M001",
  "quantity": 50.00,
  "locationCode": "A-01-01",
  "batchNumber": "B20230515001",
  "operatorId": "admin",
  "operatorName": "管理员",
  "remark": "采购入库"
}
```

**返回示例**：

```json
{
  "succeeded": true,
  "message": "入库成功",
  "data": {
    "id": 1,
    "transactionCode": "T202305151143001234",
    "materialId": 1,
    "materialCode": "M001",
    "type": 1,
    "quantity": 50.00,
    "beforeQuantity": 100.00,
    "afterQuantity": 150.00,
    "locationCode": "A-01-01",
    "batchNumber": "B20230515001",
    "operatorId": "admin",
    "operatorName": "管理员",
    "remark": "采购入库",
    "createTime": "2023-05-15T11:43:00"
  },
  "errors": []
}
```

#### 物料出库

```
POST /api/Material/outstock
```

执行物料出库操作。

**请求参数**：

```json
{
  "materialCode": "M001",
  "quantity": 30.00,
  "locationCode": "A-01-01",
  "batchNumber": "B20230515001",
  "operatorId": "admin",
  "operatorName": "管理员",
  "taskId": 101,
  "taskCode": "T10001",
  "remark": "生产领料"
}
```

**返回示例**：

```json
{
  "succeeded": true,
  "message": "出库成功",
  "data": {
    "id": 2,
    "transactionCode": "T202305151155001234",
    "materialId": 1,
    "materialCode": "M001",
    "type": 2,
    "quantity": 30.00,
    "beforeQuantity": 150.00,
    "afterQuantity": 120.00,
    "locationCode": "A-01-01",
    "batchNumber": "B20230515001",
    "operatorId": "admin",
    "operatorName": "管理员",
    "taskId": 101,
    "taskCode": "T10001",
    "remark": "生产领料",
    "createTime": "2023-05-15T11:55:00"
  },
  "errors": []
}
```

#### 库存调整

```
POST /api/Material/adjust
```

执行库存调整操作。

**请求参数**：

```json
{
  "materialCode": "M001",
  "quantity": 110.00,
  "locationCode": "A-01-01",
  "operatorId": "admin",
  "operatorName": "管理员",
  "remark": "盘点调整"
}
```

**返回示例**：

```json
{
  "succeeded": true,
  "message": "库存调整成功",
  "data": {
    "id": 3,
    "transactionCode": "T202305151210001234",
    "materialId": 1,
    "materialCode": "M001",
    "type": 3,
    "quantity": 10.00,
    "beforeQuantity": 120.00,
    "afterQuantity": 110.00,
    "locationCode": "A-01-01",
    "operatorId": "admin",
    "operatorName": "管理员",
    "remark": "库存调整 120 -> 110 ，盘点调整",
    "createTime": "2023-05-15T12:10:00"
  },
  "errors": []
}
```

#### 库内移位

```
POST /api/Material/transfer
```

执行库内移位操作。

**请求参数**：

```json
{
  "materialCode": "M001",
  "quantity": 50.00,
  "locationCode": "A-01-01",
  "targetLocationCode": "B-02-03",
  "operatorId": "admin",
  "operatorName": "管理员",
  "remark": "库位整理"
}
```

**返回示例**：

```json
{
  "succeeded": true,
  "message": "库内移位成功",
  "data": {
    "id": 4,
    "transactionCode": "T202305151230001234",
    "materialId": 1,
    "materialCode": "M001",
    "type": 4,
    "quantity": 50.00,
    "beforeQuantity": 110.00,
    "afterQuantity": 110.00,
    "locationCode": "A-01-01",
    "targetLocationCode": "B-02-03",
    "operatorId": "admin",
    "operatorName": "管理员",
    "remark": "库内移位 A-01-01 -> B-02-03 ，库位整理",
    "createTime": "2023-05-15T12:30:00"
  },
  "errors": []
}
```

### 查询功能

#### 获取物料交易历史记录

```
GET /api/Material/history/{materialCode}
```

获取指定物料的交易历史记录。

**参数**：

- `materialCode`：物料编码

**返回示例**：

```json
[
  {
    "id": 4,
    "transactionCode": "T202305151230001234",
    "materialId": 1,
    "materialCode": "M001",
    "type": 4,
    "quantity": 50.00,
    "beforeQuantity": 110.00,
    "afterQuantity": 110.00,
    "locationCode": "A-01-01",
    "targetLocationCode": "B-02-03",
    "operatorId": "admin",
    "operatorName": "管理员",
    "remark": "库内移位 A-01-01 -> B-02-03 ，库位整理",
    "createTime": "2023-05-15T12:30:00"
  },
  // 更多交易记录...
]
```

#### 获取低库存预警物料

```
GET /api/Material/lowstock
```

获取库存数量低于最小库存的物料列表。

**返回示例**：

```json
[
  {
    "id": 3,
    "code": "M003",
    "name": "电缆",
    "specification": "2×1.5mm²",
    "unit": "米",
    "quantity": 80.00,
    "minStock": 100.00,
    "maxStock": 1000.00,
    "locationCode": "C-01-01",
    "createTime": "2023-01-01T00:00:00"
  },
  // 更多低库存物料...
]
```

## 在其他服务中调用示例

### 在任务执行服务中使用物料出入库

```csharp
// 任务执行服务示例
public class TaskExecutionService
{
    private readonly IMaterialService _materialService;
    
    public TaskExecutionService(IMaterialService materialService)
    {
        _materialService = materialService;
    }
    
    public async Task ExecuteTask(TaskInfo task)
    {
        // ... 任务执行前的准备工作 ...
        
        // 执行出库操作
        var outStockResult = await _materialService.OutStockAsync(new MaterialTransactionDto
        {
            MaterialCode = task.MaterialCode,
            Quantity = task.Quantity,
            LocationCode = task.SourceLocationCode,
            BatchNumber = task.BatchNumber,
            OperatorId = task.OperatorId,
            OperatorName = task.OperatorName,
            TaskId = task.Id,
            TaskCode = task.TaskCode,
            Remark = $"任务{task.TaskCode}自动出库"
        });
        
        if (!outStockResult.Succeeded)
        {
            throw new Exception($"出库失败：{outStockResult.Message}");
        }
        
        // ... 任务执行中的其他处理 ...
        
        // 执行入库操作
        var inStockResult = await _materialService.InStockAsync(new MaterialTransactionDto
        {
            MaterialCode = task.MaterialCode,
            Quantity = task.Quantity,
            LocationCode = task.TargetLocationCode,
            BatchNumber = task.BatchNumber,
            OperatorId = task.OperatorId,
            OperatorName = task.OperatorName,
            TaskId = task.Id,
            TaskCode = task.TaskCode,
            Remark = $"任务{task.TaskCode}自动入库"
        });
        
        if (!inStockResult.Succeeded)
        {
            throw new Exception($"入库失败：{inStockResult.Message}");
        }
        
        // ... 任务执行完成后的收尾工作 ...
    }
}
```

## 注意事项

1. 物料编码必须唯一，添加物料时会检查编码是否已存在。
2. 出库操作会检查库存是否足够，不足时会返回错误。
3. 所有物料交易操作都会生成交易记录，便于追踪和审计。
4. 库内移位操作不会改变物料的总库存数量，只会更新储位信息。
5. 所有操作都使用数据库事务，确保数据一致性。
6. 在进行物料操作前，请确保物料信息已正确添加到系统中。

## 错误处理

所有API接口都会返回统一格式的响应，包含操作是否成功、消息和数据。当操作失败时，会在响应中返回具体的错误信息。

错误可能包括：
- 物料不存在
- 物料编码已存在
- 库存不足
- 储位不存在
- 其他数据库操作错误

## 数据库索引说明

为提高查询性能，物料管理模块在数据库表上创建了以下索引：

### Materials表

- 主键索引：`PK_Materials` (Id)
- 唯一索引：`UK_Materials_Code` (Code)
- 普通索引：`IX_Materials_LocationCode` (LocationCode)
- 普通索引：`IX_Materials_Quantity` (Quantity)

### RCS_MaterialTransactions表

- 主键索引：`PK_RCS_MaterialTransactions` (Id)
- 唯一索引：`UK_RCS_MaterialTransactions_TransactionCode` (TransactionCode)
- 普通索引：`IX_RCS_MaterialTransactions_MaterialCode` (MaterialCode)
- 普通索引：`IX_RCS_MaterialTransactions_CreateTime` (CreateTime DESC)
- 普通索引：`IX_RCS_MaterialTransactions_Type` (Type)
- 普通索引：`IX_RCS_MaterialTransactions_LocationCode` (LocationCode)
- 条件索引：`IX_RCS_MaterialTransactions_TaskId` (TaskId) WHERE TaskId IS NOT NULL 