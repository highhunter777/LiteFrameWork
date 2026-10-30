using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LiteFramework;
using MetaServer.Contracts.Auth;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

using MetaError = MetaWire.MetaError;

namespace MetaServer
{
    /// <summary>
    /// 端点层收口：统一错误体工厂 + **全服务唯一 JSON 出口** + 请求体解析收口。
    ///
    /// **为什么 JSON 出口必须唯一**：Minimal API 的 `Results.Json` / `ReadFromJsonAsync` 走
    /// System.Text.Json，而本服务改用 Newtonsoft（与客户端同一库、同一份 <c>WireJsonSettings</c>）。
    /// 只要有一个端点绕过本类直调 `Results.Json`，同一服务就会出现两套字段名/转义/日期口径——
    /// 那是比"多写几行"贵得多的分叉。**端点不得直调 `Results.Json` / `WriteAsJsonAsync`。**
    ///
    /// 形状契约（全服务统一）：成功体 = 各契约 DTO；错误体 = <c>{ code, messageKey, args }</c>，
    /// `args` 缺省空数组（杜绝"有的端点有 args、有的没有"这种隐性形状分叉）。
    /// </summary>
    public static class MetaResults
    {
        /// <summary>契约 JSON 的 content-type（显式带 charset：`Results.Text` 不会自动补）。</summary>
        public const string JsonContentType = "application/json; charset=utf-8";

        /// <summary>成功响应（2xx）：按契约设置序列化——**唯一的成功体出口**。</summary>
        public static IResult Ok(object payload, int statusCode = StatusCodes.Status200OK)
            => Results.Text(JsonConvert.SerializeObject(payload, WireJsonSettings.Value),
                JsonContentType, Encoding.UTF8, statusCode);

        /// <summary>错误体（args 缺省空数组；**绝不**放令牌/字段原文/凭据）。</summary>
        public static MetaError Error(string code, string messageKey, params string[] args)
        {
            return new MetaError
            {
                Code = code,
                MessageKey = messageKey,
                Args = new List<string>(args ?? Array.Empty<string>()),
            };
        }

        /// <summary>错误结果（错误体 + statusCode）。</summary>
        public static IResult Error(int statusCode, string code, string messageKey, params string[] args)
        {
            return Ok(Error(code, messageKey, args), statusCode);
        }

        /// <summary>错误结果（args 为列表——字段级错误"一次报全"的既有形态）。</summary>
        public static IResult Error(int statusCode, string code, string messageKey, IReadOnlyList<string> args)
        {
            return Ok(new MetaError
            {
                Code = code,
                MessageKey = messageKey,
                Args = new List<string>(args ?? Array.Empty<string>()),
            }, statusCode);
        }

        /// <summary>功能未配置（服务缺席）——稳定 503，不回退假数据（§10）。</summary>
        public static IResult Disabled(string code, string messageKey)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, code, messageKey);
        }

        /// <summary>实例凭据无效/缺失（缺失与坏形状不区分对待）——401。</summary>
        public static IResult InstanceUnauthorized(string code, string messageKey)
        {
            return Error(StatusCodes.Status401Unauthorized, code, messageKey);
        }

        /// <summary>存储未确认（提交失败/响应丢失——已应用也可能没有，重试安全）——503。</summary>
        public static IResult StoreUnconfirmed(string code, string messageKey)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, code, messageKey);
        }

        /// <summary>请求载荷非法——400（<paramref name="args"/> 可带**字段名/上限值**，不回显字段值）。</summary>
        public static IResult InvalidRequest(string code, string messageKey, params string[] args)
        {
            return Error(StatusCodes.Status400BadRequest, code, messageKey, args);
        }

        /// <summary>请求载荷非法——400（字段错误列表形态）。</summary>
        public static IResult InvalidRequest(string code, string messageKey, IReadOnlyList<string> args)
        {
            return Error(StatusCodes.Status400BadRequest, code, messageKey, args);
        }

        /// <summary>
        /// 直接写响应体（中间件用——限流等不经端点的路径也必须走同一 JSON 口径）。
        /// 调用方负责先设自定义头（如 `Retry-After`）：本方法会先写状态码与 content-type 再写体。
        /// </summary>
        public static async Task WriteAsync(HttpContext context, object payload, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = JsonContentType;
            await context.Response.WriteAsync(JsonConvert.SerializeObject(payload, WireJsonSettings.Value));
        }

        /// <summary>
        /// 请求体解析收口：坏 JSON / 非 JSON content-type / 形状解码失败 / 未知字段 →
        /// 返回 null（调用方按 InvalidRequest 收口，**不 500**）；请求中止随管道传播。
        ///
        /// 与 STJ 版本的差异只在实现：未知字段现在会**解码失败**（`MissingMemberHandling.Error`），
        /// 这是刻意的——契约漂移应当立刻可见，而不是被静默忽略成"字段永远是 null"。
        /// </summary>
        public static async Task<T> TryReadJsonAsync<T>(HttpContext context) where T : class
        {
            try
            {
                if (!context.Request.HasJsonContentType()) return null;

                string json;
                using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8))
                    json = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonConvert.DeserializeObject<T>(json, WireJsonSettings.Value);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
