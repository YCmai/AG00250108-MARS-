-- 查询数据库中的用户密码信息
SELECT 
    Username,
    PasswordHash,
    LEN(PasswordHash) as PasswordLength,
    FullName,
    IsAdmin,
    IsActive
FROM Users 
WHERE Username = 'admin';

-- 如果需要更新密码，可以使用以下语句
-- UPDATE Users 
-- SET PasswordHash = 'YWRtaW4xMjM='  -- admin123的Base64编码
-- WHERE Username = 'admin';
