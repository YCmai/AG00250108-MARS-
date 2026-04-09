using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using static System.Net.WebRequestMethods;
using Microsoft.Extensions.Logging;
using Dapper;
using WarehouseManagementSystem.Db;
using OfficeOpenXml;
using System.IO;
using System.Text.RegularExpressions;

public class DisplayLocationController : Controller
{
    private readonly ILocationService _locationService;
    private readonly ILogger<DisplayLocationController> _logger;

    public DisplayLocationController(ILocationService locationService, ILogger<DisplayLocationController> logger)
    {
        _locationService = locationService;
        _logger = logger;
    }

    // 获取储位列表页面，支持搜索和分页
    public async Task<IActionResult> Index(string searchString, int page = 1)
    {
        try
        {
            int pageSize = 5000;
            var (items, totalItems) = await _locationService.GetLocations(searchString, page, pageSize);
            var (available, used) = await _locationService.GetStorageCapacityStats();

            ViewData["StorageCapacityAvailable"] = available;
            ViewData["StorageCapacityUse"] = used;

            // 分组自然排序 - Group natural sorting
            var groupList = items
                .Select(l => l.Group)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct()
                .OrderBy(g => Regex.Matches(g, "\\d+").Cast<Match>().Select(m => int.Parse(m.Value)).ToList(), new ListIntComparer())
                .ToList();
            ViewData["GroupList"] = groupList.ToArray();

            return View(new PagedResult<RCS_Locations>
            {
                Items = items.ToList(),
                TotalItems = totalItems,
                PageNumber = page,
                TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get location list - 获取库位列表失败");
            return View(new PagedResult<RCS_Locations>());
        }
    }

    public class ListIntComparer : IComparer<List<int>>
    {
        public int Compare(List<int> x, List<int> y)
        {
            int minLen = Math.Min(x.Count, y.Count);
            for (int i = 0; i < minLen; i++)
            {
                int cmp = x[i].CompareTo(y[i]);
                if (cmp != 0) return cmp;
            }
            return x.Count.CompareTo(y.Count);
        }
    }


    // 创建或编辑储位页面
    public async Task<IActionResult> CreateEdit(int? id)
    {
        try
        {
            if (id == null)
            {
                return View(new RCS_Locations());
            }

            var location = await _locationService.GetLocationById(id.Value);
            if (location == null)
            {
                return NotFound();
            }

            return View(location);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get location info - 获取库位信息失败");
            TempData["Message"] = "Failed to get location info! Please try again later.";
            TempData["MessageType"] = "danger";
            return View(new RCS_Locations());
        }
    }

    // 保存储位信息（创建或更新）
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEdit(RCS_Locations location)
    {
        try
        {
            var (success, message) = await _locationService.CreateOrUpdateLocation(location);
            
            TempData["Message"] = message;
            TempData["MessageType"] = success ? "success" : "danger";
            if (success)
            {
                TempData["RedirectAfterDelay"] = true;
            }
            
            return View(location);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save location info - 保存库位信息失败");
            TempData["Message"] = "Save failed, please try again later.";
            TempData["MessageType"] = "danger";
            return View(location);
        }
    }

    // 删除确认操作
    [HttpPost]
    public async Task<IActionResult> DeleteConfirmed(int id, int type)
    {
        try
        {
            var (success, message) = await _locationService.HandleLocationOperation(id, type);
            return Json(new { success, message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation failed - 操作失败");
            return Json(new { success = false, message = "Operation failed, please try again later." });
        }
    }
    
    // 批量清空物料 - 修改批量操作方法，接受储位ID列表而不仅仅是区域
    [HttpPost]
    public async Task<IActionResult> BatchClearMaterials(List<int> locationIds)
    {
        try
        {
            if (locationIds == null || !locationIds.Any())
            {
                return Json(new { success = false, message = "Please select locations to operate" });
            }
            
            var (success, message, affectedCount) = await _locationService.BatchClearMaterialsByIds(locationIds);
            return Json(new { success, message, affectedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Batch clear materials failed - 批量清空储位物料失败");
            return Json(new { success = false, message = "Batch operation failed, please try again later." });
        }
    }

    // 批量锁定/解锁储位
    [HttpPost]
    public async Task<IActionResult> BatchToggleLock(List<int> locationIds, bool lockState)
    {
        try
        {
            if (locationIds == null || !locationIds.Any())
            {
                return Json(new { success = false, message = "Please select locations to operate" });
            }
            
            var (success, message, affectedCount) = await _locationService.BatchToggleLockByIds(locationIds, lockState);
            return Json(new { success, message, affectedCount });
        }
        catch (Exception ex)
        {
            string operation = lockState ? "lock" : "unlock";
            _logger.LogError(ex, $"Batch {operation} locations failed - 批量{(lockState ? "锁定" : "解锁")}储位失败");
            return Json(new { success = false, message = $"Batch {operation} failed, please try again later." });
        }
    }

    // 按区域批量清空物料 - 保留原有的按区域批量操作方法
    [HttpPost]
    public async Task<IActionResult> BatchClearMaterialsByGroup(string group)
    {
        try
        {
            if (string.IsNullOrEmpty(group))
            {
                return Json(new { success = false, message = "Please specify the area to operate" });
            }
            
            var (success, message, affectedCount) = await _locationService.BatchClearMaterials(group);
            return Json(new { success, message, affectedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Batch clear materials in area {group} failed - 批量清空区域 {group} 的物料失败");
            return Json(new { success = false, message = "Batch operation failed, please try again later." });
        }
    }

    // 按区域批量锁定/解锁储位
    [HttpPost]
    public async Task<IActionResult> BatchToggleLockByGroup(string group, bool lockState)
    {
        try
        {
            if (string.IsNullOrEmpty(group))
            {
                return Json(new { success = false, message = "Please specify the area to operate" });
            }
            
            var (success, message, affectedCount) = await _locationService.BatchToggleLock(group, lockState);
            return Json(new { success, message, affectedCount });
        }
        catch (Exception ex)
        {
            string operation = lockState ? "lock" : "unlock";
            _logger.LogError(ex, $"Batch {operation} area {group} locations failed - 批量{(lockState ? "锁定" : "解锁")}区域 {group} 的储位失败");
            return Json(new { success = false, message = $"Batch {operation} failed, please try again later." });
        }
    }

    // 批量启用/禁用储位
    [HttpPost]
    public async Task<IActionResult> BatchToggleEnabled(List<int> locationIds, bool enabledState)
    {
        try
        {
            if (locationIds == null || !locationIds.Any())
            {
                return Json(new { success = false, message = "Please select locations to operate" });
            }
            
            var (success, message, affectedCount) = await _locationService.BatchToggleEnabledByIds(locationIds, enabledState);
            return Json(new { success, message, affectedCount });
        }
        catch (Exception ex)
        {
            string operation = enabledState ? "enable" : "disable";
            _logger.LogError(ex, $"Batch {operation} locations failed - 批量{(enabledState ? "启用" : "禁用")}储位失败");
            return Json(new { success = false, message = $"Batch {operation} failed, please try again later." });
        }
    }

    // 按区域批量启用/禁用储位
    [HttpPost]
    public async Task<IActionResult> BatchToggleEnabledByGroup(string group, bool enabledState)
    {
        try
        {
            if (string.IsNullOrEmpty(group))
            {
                return Json(new { success = false, message = "Please specify the area to operate" });
            }
            
            var (success, message, affectedCount) = await _locationService.BatchToggleEnabled(group, enabledState);
            return Json(new { success, message, affectedCount });
        }
        catch (Exception ex)
        {
            string operation = enabledState ? "enable" : "disable";
            _logger.LogError(ex, $"Batch {operation} area {group} locations failed - 批量{(enabledState ? "启用" : "禁用")}区域 {group} 的储位失败");
            return Json(new { success = false, message = $"Batch {operation} failed, please try again later." });
        }
    }

    // 下载导入模板
    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        try
        {
            // 设置EPPlus许可证上下文
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Location Import Template");
                
                // 添加表头 - Add headers
                worksheet.Cells[1, 1].Value = "Map Node";
                worksheet.Cells[1, 2].Value = "Node Remark";
                worksheet.Cells[1, 3].Value = "Operation Point";
                worksheet.Cells[1, 4].Value = "Group";
                worksheet.Cells[1, 5].Value = "Lifting Height";
                worksheet.Cells[1, 6].Value = "Unload Height";
                
                // 设置列宽 - Set column width
                worksheet.Column(1).Width = 15;
                worksheet.Column(2).Width = 15;
                worksheet.Column(3).Width = 15;
                worksheet.Column(4).Width = 15;
                worksheet.Column(5).Width = 15;
                worksheet.Column(6).Width = 15;
                
                // 添加示例数据 - Add sample data
                worksheet.Cells[2, 1].Value = "A001";
                worksheet.Cells[2, 2].Value = "A区-01";
                worksheet.Cells[2, 3].Value = "OP001";
                worksheet.Cells[2, 4].Value = "Area A";
                worksheet.Cells[2, 5].Value = "100";
                worksheet.Cells[2, 6].Value = "50";
                
                // 设置表头样式 - Set header style
                var headerRange = worksheet.Cells[1, 1, 1, 6];
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                
                // 设置所有单元格的边框 - Set borders for all cells
                var dataRange = worksheet.Cells[1, 1, 2, 6];
                dataRange.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                dataRange.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                dataRange.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                dataRange.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                
                // 生成文件 - Generate file
                var fileBytes = package.GetAsByteArray();
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Location_Import_Template.xlsx");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download template file failed - 下载模板文件失败");
            return Json(new { success = false, message = "Download template failed, please try again later" });
        }
    }

    // 预览Excel文件
    [HttpPost]
    public async Task<IActionResult> PreviewExcel(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return Json(new { success = false, message = "Please select a file to upload" });
            }

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension.Rows;
                    var colCount = worksheet.Dimension.Columns;

                    // 获取表头
                    var headers = new List<string>();
                    for (int col = 1; col <= colCount; col++)
                    {
                        headers.Add(worksheet.Cells[1, col].Text);
                    }

                    // 获取前10行数据作为预览
                    var previewRows = new List<List<string>>();
                    for (int row = 2; row <= Math.Min(11, rowCount); row++)
                    {
                        var rowData = new List<string>();
                        for (int col = 1; col <= colCount; col++)
                        {
                            rowData.Add(worksheet.Cells[row, col].Text);
                        }
                        previewRows.Add(rowData);
                    }

                    return Json(new { 
                        success = true, 
                        preview = new { 
                            headers = headers, 
                            rows = previewRows,
                            totalRows = rowCount - 1 // 减去表头行 - Subtract header row
                        } 
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview Excel file failed - 预览Excel文件失败");
            return Json(new { success = false, message = "Preview failed, please check if the file format is correct" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ImportExcel(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return Json(new { success = false, message = "Please select a file to upload" });
            }

            // 设置EPPlus许可证上下文
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension.Rows;
                    var colCount = worksheet.Dimension.Columns;

                    // 验证表头
                    var headers = new List<string>();
                    for (int col = 1; col <= colCount; col++)
                    {
                        headers.Add(worksheet.Cells[1, col].Text.Trim());
                    }

                    // 检查必要的列是否存在
                    var requiredColumns = new[] { "地图节点", "节点备注", "操作点", "分组" };
                    var missingColumns = requiredColumns.Where(col => !headers.Contains(col)).ToList();
                    if (missingColumns.Any())
                    {
                        return Json(new { success = false, message = $"模板缺少必要的列: {string.Join(", ", missingColumns)}" });
                    }

                    // 获取列索引
                    var nodeIndex = headers.IndexOf("地图节点") + 1;
                    var nodeRemarkIndex = headers.IndexOf("节点备注") + 1;
                    var operationPointIndex = headers.IndexOf("操作点") + 1;
                    var groupIndex = headers.IndexOf("分组") + 1;

                    // 检查是否有数据行
                    if (rowCount <= 1)
                    {
                        return Json(new { success = false, message = "Excel文件中没有数据行" });
                    }

                    // 获取所有现有的节点和节点备注，用于检查重复
                    var existingLocations = await _locationService.GetLocations("", 1, int.MaxValue);
                    var existingNodes = existingLocations.Items.Select(l => l.Name).ToList();
                    var existingNodeRemarks = existingLocations.Items.Select(l => l.NodeRemark).ToList();

                    var locations = new List<RCS_Locations>();
                    var errors = new List<string>();
                    var duplicateNodes = new HashSet<string>();
                    var duplicateNodeRemarks = new HashSet<string>();

                    // 从第2行开始读取数据（跳过表头）
                    for (int row = 2; row <= rowCount; row++)
                    {
                        try
                        {
                            // 获取并清理数据
                            var node = worksheet.Cells[row, nodeIndex].Text.Trim();
                            var nodeRemark = worksheet.Cells[row, nodeRemarkIndex].Text.Trim();
                            var operationPoint = worksheet.Cells[row, operationPointIndex].Text.Trim();
                            var group = worksheet.Cells[row, groupIndex].Text.Trim();
                            
                            // 获取举升高度和卸载高度（可选字段）
                            var liftingHeightText = headers.Contains("举升高度") ? worksheet.Cells[row, headers.IndexOf("举升高度") + 1].Text?.Trim() : "";
                            var unloadHeightText = headers.Contains("卸载高度") ? worksheet.Cells[row, headers.IndexOf("卸载高度") + 1].Text?.Trim() : "";
                            
                            int liftingHeight = 0;
                            int unloadHeight = 0;
                            
                            if (!string.IsNullOrEmpty(liftingHeightText) && !int.TryParse(liftingHeightText, out liftingHeight))
                            {
                                errors.Add($"第{row}行：举升高度必须是数字");
                                continue;
                            }
                            
                            if (!string.IsNullOrEmpty(unloadHeightText) && !int.TryParse(unloadHeightText, out unloadHeight))
                            {
                                errors.Add($"第{row}行：卸载高度必须是数字");
                                continue;
                            }

                            // 验证必填字段
                            if (string.IsNullOrEmpty(node))
                            {
                                errors.Add($"第{row}行：地图节点不能为空");
                                continue;
                            }

                            if (string.IsNullOrEmpty(nodeRemark))
                            {
                                errors.Add($"第{row}行：节点备注不能为空");
                                continue;
                            }

                            if (string.IsNullOrEmpty(operationPoint))
                            {
                                errors.Add($"第{row}行：操作点不能为空");
                                continue;
                            }

                            if (string.IsNullOrEmpty(group))
                            {
                                errors.Add($"第{row}行：分组不能为空");
                                continue;
                            }

                            // 检查节点和节点备注是否重复
                            if (existingNodes.Contains(node) || duplicateNodes.Contains(node))
                            {
                                errors.Add($"第{row}行：地图节点 '{node}' 已存在");
                                duplicateNodes.Add(node);
                                continue;
                            }

                            if (existingNodeRemarks.Contains(nodeRemark) || duplicateNodeRemarks.Contains(nodeRemark))
                            {
                                errors.Add($"第{row}行：节点备注 '{nodeRemark}' 已存在");
                                duplicateNodeRemarks.Add(nodeRemark);
                                continue;
                            }

                            // 添加到重复检查集合
                            duplicateNodes.Add(node);
                            duplicateNodeRemarks.Add(nodeRemark);

                            // 创建储位对象，设置默认值
                            var location = new RCS_Locations
                            {
                                Name = node,
                                NodeRemark = nodeRemark,
                                WattingNode = operationPoint,  // 设置操作点为WattingNode
                                Group = group,
                                LiftingHeight = liftingHeight,
                                UnloadHeight = unloadHeight,
                                MaterialCode = null,  // 默认值为空
                                PalletID = "0",      // 默认值为空
                                Weight = "0",         // 默认值为0
                                Quanitity = "0",      // 默认值为0
                                EntryDate = null,     // 默认值为空
                                Lock = false,         // 默认值为false
                                Enabled = true        // 默认值为启用
                            };

                            locations.Add(location);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"第{row}行：数据格式错误 - {ex.Message}");
                        }
                    }

                    if (errors.Any())
                    {
                        return Json(new { success = false, message = string.Join("\n", errors) });
                    }

                    // 批量保存储位
                    int successCount = 0;
                    foreach (var location in locations)
                    {
                        var (success, _) = await _locationService.CreateOrUpdateLocation(location);
                        if (success)
                        {
                            successCount++;
                        }
                    }

                    return Json(new { 
                        success = true, 
                        message = $"成功导入 {successCount} 个储位",
                        count = successCount
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入Excel文件失败");
            return Json(new { success = false, message = "导入失败，请检查文件格式是否正确" });
        }
    }
}
