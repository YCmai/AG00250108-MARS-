-- 创建物料表
CREATE TABLE [dbo].[Materials] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Code] NVARCHAR(50) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Specification] NVARCHAR(100) NULL,
    [Unit] NVARCHAR(20) NULL,
    [Quantity] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [MinStock] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [MaxStock] DECIMAL(18, 2) NOT NULL DEFAULT 0,
    [LocationCode] NVARCHAR(50) NULL,
    [ImageUrl] NVARCHAR(255) NULL,
    [CreateTime] DATETIME NOT NULL DEFAULT GETDATE(),
    [UpdateTime] DATETIME NULL,
    [Remark] NVARCHAR(500) NULL,
    CONSTRAINT [PK_Materials] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UK_Materials_Code] UNIQUE NONCLUSTERED ([Code] ASC)
);

-- 创建物料索引
CREATE NONCLUSTERED INDEX [IX_Materials_LocationCode] ON [dbo].[Materials]([LocationCode] ASC);
CREATE NONCLUSTERED INDEX [IX_Materials_Quantity] ON [dbo].[Materials]([Quantity] ASC);

-- 创建物料交易记录表
CREATE TABLE [dbo].[RCS_MaterialTransactions] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [TransactionCode] NVARCHAR(50) NOT NULL,
    [MaterialId] INT NOT NULL,
    [MaterialCode] NVARCHAR(50) NOT NULL,
    [Type] INT NOT NULL,
    [Quantity] DECIMAL(18, 2) NOT NULL,
    [BeforeQuantity] DECIMAL(18, 2) NOT NULL,
    [AfterQuantity] DECIMAL(18, 2) NOT NULL,
    [LocationCode] NVARCHAR(50) NULL,
    [TargetLocationCode] NVARCHAR(50) NULL,
    [BatchNumber] NVARCHAR(50) NULL,
    [OperatorId] NVARCHAR(50) NULL,
    [OperatorName] NVARCHAR(50) NULL,
    [TaskId] INT NULL,
    [TaskCode] NVARCHAR(50) NULL,
    [Remark] NVARCHAR(500) NULL,
    [CreateTime] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_RCS_MaterialTransactions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UK_RCS_MaterialTransactions_TransactionCode] UNIQUE NONCLUSTERED ([TransactionCode] ASC),
    CONSTRAINT [FK_RCS_MaterialTransactions_Materials] FOREIGN KEY ([MaterialId]) REFERENCES [dbo].[Materials] ([Id]) ON DELETE NO ACTION
);

-- 创建物料交易记录索引
CREATE NONCLUSTERED INDEX [IX_RCS_MaterialTransactions_MaterialCode] ON [dbo].[RCS_MaterialTransactions]([MaterialCode] ASC);
CREATE NONCLUSTERED INDEX [IX_RCS_MaterialTransactions_CreateTime] ON [dbo].[RCS_MaterialTransactions]([CreateTime] DESC);
CREATE NONCLUSTERED INDEX [IX_RCS_MaterialTransactions_Type] ON [dbo].[RCS_MaterialTransactions]([Type] ASC);
CREATE NONCLUSTERED INDEX [IX_RCS_MaterialTransactions_LocationCode] ON [dbo].[RCS_MaterialTransactions]([LocationCode] ASC);
CREATE NONCLUSTERED INDEX [IX_RCS_MaterialTransactions_TaskId] ON [dbo].[RCS_MaterialTransactions]([TaskId] ASC) WHERE [TaskId] IS NOT NULL;

-- 添加注释
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'物料信息表', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'物料ID', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'Id';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'物料编码', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'Code';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'物料名称', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'Name';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'规格型号', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'Specification';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'单位', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'Unit';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'库存数量', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'Quantity';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'最小库存', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'MinStock';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'最大库存', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'MaxStock';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'储位编码', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'LocationCode';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'物料图片', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'ImageUrl';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'创建时间', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'CreateTime';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'更新时间', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'UpdateTime';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'备注', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Materials', @level2type = N'COLUMN', @level2name = N'Remark';

EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'物料交易记录表', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'交易记录ID', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'Id';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'交易单号', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'TransactionCode';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'物料ID', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'MaterialId';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'物料编码', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'MaterialCode';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'交易类型（1-入库，2-出库，3-调整，4-移位，5-盘点）', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'Type';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'交易数量', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'Quantity';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'交易前库存', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'BeforeQuantity';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'交易后库存', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'AfterQuantity';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'储位编码', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'LocationCode';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'目标储位编码', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'TargetLocationCode';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'批次号', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'BatchNumber';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'操作人ID', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'OperatorId';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'操作人姓名', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'OperatorName';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'任务ID', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'TaskId';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'任务编号', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'TaskCode';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'备注', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'Remark';
EXEC sp_addextendedproperty @name = N'MS_Description', @value = N'创建时间', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RCS_MaterialTransactions', @level2type = N'COLUMN', @level2name = N'CreateTime'; 