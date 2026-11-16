using Microsoft.AspNetCore.Antiforgery;

namespace Snet.Yolo.Tasks.Services;

/// <summary>认证后的分块上传接口；用户、目标和文件清单在创建时已固定。</summary>
public static class TransferEndpoints
{
    /// <summary>注册权威偏移查询、分块接收、开始导入与取消端点。</summary>
    public static void MapPersistentTransfers(this WebApplication app)
    {
        app.MapGet("/api/transfers/downloads/{id}", (HttpContext context, string id, PersistentDownloadService downloads) =>
            downloads.Find(context.User.Identity!.Name!, id) is { } job ? Results.Ok(PersistentDownloadService.View(job)) : Results.NotFound()).RequireAuthorization();
        app.MapGet("/api/transfers/downloads/{id}/file", (HttpContext context, string id, PersistentDownloadService downloads) =>
            downloads.Find(context.User.Identity!.Name!, id) is { } job ? downloads.FileResult(context, job) : Results.NotFound()).RequireAuthorization();
        app.MapPost("/api/transfers/downloads/{id}/cancel", async (HttpContext context, string id, PersistentDownloadService downloads, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) { return Results.BadRequest("防伪令牌无效。"); }
            if (downloads.Find(context.User.Identity!.Name!, id) is not { } job) { return Results.NotFound(); }
            downloads.Cancel(job); return Results.Ok(PersistentDownloadService.View(job));
        }).RequireAuthorization();
        app.MapGet("/api/transfers/token", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken })).RequireAuthorization();
        app.MapGet("/api/transfers/uploads", (HttpContext context, PersistentUploadService transfers) =>
            Results.Ok(transfers.List(context.User.Identity!.Name!).Select(View))).RequireAuthorization();
        app.MapGet("/api/transfers/uploads/{id}", (HttpContext context, string id, PersistentUploadService transfers) =>
            transfers.Find(context.User.Identity!.Name!, id) is { } job ? Results.Ok(View(job)) : Results.NotFound()).RequireAuthorization();
        app.MapPut("/api/transfers/uploads/{id}/{index:int}", async (HttpContext context, string id, int index, long offset, PersistentUploadService transfers, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) { return Results.BadRequest("防伪令牌无效。"); }
            var job = transfers.Find(context.User.Identity!.Name!, id);
            if (job is null) { return Results.NotFound(); }
            if (context.Request.ContentLength > PersistentUploadService.ChunkBytes) { return Results.BadRequest("分块超过 4 MiB。"); }
            try { await transfers.AppendAsync(job, index, offset, context.Request.Body, context.RequestAborted); return Results.Ok(View(job)); }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested) { return Results.Conflict(new { error = "上传已取消。" }); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (InvalidDataException error) { return Results.BadRequest(new { error = error.Message }); }
        }).RequireAuthorization();
        app.MapPost("/api/transfers/uploads/{id}/complete", async (HttpContext context, string id, PersistentUploadService transfers, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) { return Results.BadRequest("防伪令牌无效。"); }
            var job = transfers.Find(context.User.Identity!.Name!, id);
            if (job is null) { return Results.NotFound(); }
            try { await transfers.CompleteAsync(job, context.User); return Results.Ok(View(job)); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        }).RequireAuthorization();
        app.MapPost("/api/transfers/uploads/{id}/cancel", async (HttpContext context, string id, PersistentUploadService transfers, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) { return Results.BadRequest("防伪令牌无效。"); }
            var job = transfers.Find(context.User.Identity!.Name!, id);
            if (job is null) { return Results.NotFound(); }
            await transfers.CancelAsync(job); return Results.Ok(View(job));
        }).RequireAuthorization();
    }

    /// <summary>仅返回浏览器恢复传输需要的字段，不暴露磁盘路径或其它用户身份。</summary>
    private static object View(PersistentUpload job) => new { job.Id, job.State, job.Files, job.Offsets, job.Error };
}
