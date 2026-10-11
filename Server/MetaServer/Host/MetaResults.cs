using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MetaServer.Contracts.Auth;
using Microsoft.AspNetCore.Http;

namespace MetaServer
{
    /// <summary>
    /// 端点层收口（P2 单源）：统一错误体工厂 + 标准结果助手 + 请求体解析收口。
    ///
    /// **为什么要有它**：`new MetaError { Code = …, MessageKey = …, Args = … }` 的样板此前在每个端点各写一遍
    /// （约 15 处）、`Disabled/InstanceUnauthorized/StoreUnconfirmed` 在 Lobby 与 Settlement 各有一份同构私有助手、
    /// 请求体 try/catch 收口块四份近似拷贝且异常面不一致。样板散落的真实代价不是冗长，而是**同一语义的
    /// 响应体形状/状态码/异常面在两条端点上分叉**——客户端无法统一分支（§5.2 统一错误形状的初衷）。
    ///
    /// **形状契约**（全服务统一，客户端按 <c>code</c> 分支、`messageKey` 交本地化、`args` 只放非敏感参数）：
    /// <c>{ code, messageKey, args }</c>；`args` **缺省空数组**（调用方不传即空——杜绝"有的端点有 args、
    /// 有的没有"这种隐性形状分叉）。
    /// </summary>
    public static class MetaResults
    {
        /// <summary>错误体（args 缺省空数组；**绝不**放令牌/字段原文/凭据）。</summary>
        public static MetaError Error(string code, string messageKey, params string[] args)
        {
            return new MetaError
            {
                Code = code,
                MessageKey = messageKey,
                Args = args ?? Array.Empty<string>(),
            };
        }

        /// <summary>错误结果（错误体 + statusCode）。</summary>
        public static IResult Error(int statusCode, string code, string messageKey, params string[] args)
        {
            return Results.Json(Error(code, messageKey, args), statusCode: statusCode);
        }

        /// <summary>错误结果（args 为列表——字段级错误"一次报全"的既有形态）。</summary>
        public static IResult Error(int statusCode, string code, string messageKey, IReadOnlyList<string> args)
        {
            return Results.Json(new MetaError
            {
                Code = code,
                MessageKey = messageKey,
                Args = args ?? Array.Empty<string>(),
            }, statusCode: statusCode);
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
        /// 请求体解析收口（四份近似拷贝归一）：坏 JSON / 非 JSON content-type / 形状解码失败 →
        /// 返回 null（调用方按 InvalidRequest 收口，**不 500**）；请求中止（<see cref="OperationCanceledException"/>）
        /// 随管道传播。
        ///
        /// 异常面为何必须 catch 宽：<c>ReadFromJsonAsync</c> 对非 JSON content-type 抛
        /// <see cref="InvalidOperationException"/> 而非 <c>JsonException</c>——只捕后者的端点会把
        /// "带 text/plain 的同形状请求"变成 500（P1③ 已修一处）。
        /// </summary>
        public static async Task<T> TryReadJsonAsync<T>(HttpContext context) where T : class
        {
            try
            {
                return await context.Request.ReadFromJsonAsync<T>();
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
