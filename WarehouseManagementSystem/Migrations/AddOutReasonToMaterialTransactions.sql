-- 为RCS_MaterialTransactions表添加OutReason字段
IF NOT EXISTS (SELECT * FROM sys.columns 
                WHERE object_id = OBJECT_ID(N'[dbo].[RCS_MaterialTransactions]') 
                AND name = 'OutReason')
BEGIN
    ALTER TABLE [dbo].[RCS_MaterialTransactions] 
    ADD [OutReason] NVARCHAR(100) NULL;

    EXEC sp_addextendedproperty 
        @name = N'MS_Description', 
        @value = N'出库原因', 
        @level0type = N'SCHEMA', 
        @level0name = N'dbo', 
        @level1type = N'TABLE', 
        @level1name = N'RCS_MaterialTransactions', 
        @level2type = N'COLUMN', 
        @level2name = N'OutReason';

    PRINT 'OutReason column added to RCS_MaterialTransactions table';
END
ELSE
BEGIN
    PRINT 'OutReason column already exists in RCS_MaterialTransactions table';
END 