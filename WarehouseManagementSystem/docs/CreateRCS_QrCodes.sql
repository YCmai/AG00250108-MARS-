IF OBJECT_ID(N'dbo.RCS_QrCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RCS_QrCodes
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RCS_QrCodes PRIMARY KEY,
        QRCode NVARCHAR(256) NOT NULL CONSTRAINT DF_RCS_QrCodes_QRCode DEFAULT (N''),
        CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_RCS_QrCodes_CreateTime DEFAULT (SYSDATETIME()),
        CarIP NVARCHAR(64) NOT NULL,
        TaskType INT NOT NULL CONSTRAINT DF_RCS_QrCodes_TaskType DEFAULT (0),
        Normal BIT NOT NULL CONSTRAINT DF_RCS_QrCodes_Normal DEFAULT (0),
        IfSend BIT NOT NULL CONSTRAINT DF_RCS_QrCodes_IfSend DEFAULT (0),
        Remark NVARCHAR(100) NULL,
        Excute BIT NOT NULL CONSTRAINT DF_RCS_QrCodes_Excute DEFAULT (0)
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_RCS_QrCodes_CarIP_Excute'
      AND object_id = OBJECT_ID(N'dbo.RCS_QrCodes')
)
BEGIN
    CREATE INDEX IX_RCS_QrCodes_CarIP_Excute
    ON dbo.RCS_QrCodes (CarIP, Excute)
    INCLUDE (CreateTime, QRCode);
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_RCS_QrCodes_CreateTime'
      AND object_id = OBJECT_ID(N'dbo.RCS_QrCodes')
)
BEGIN
    CREATE INDEX IX_RCS_QrCodes_CreateTime
    ON dbo.RCS_QrCodes (CreateTime DESC);
END;
GO
