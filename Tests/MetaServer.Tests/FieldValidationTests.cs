using System.Collections.Generic;
using MetaServer.Contracts.Validation;
using LiteTesting;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 字段边界校验单源用例（P2）：规则「非空白 + UTF-8 字节上限」与上限表值一起钉住。
    ///
    /// 本组的存在理由：该规则此前在 6 处以两种口径手写（4 处按 UTF-8 字节、2 处按字符数），
    /// 而**按字符数在多字节输入下形同虚设**（40 个汉字 = 40 字符但 120 字节）——
    /// 下面第一条用例就是那个判别式。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class FieldValidationTests
    {
        [Fact]
        public void 按UTF8字节判而非字符数_多字节输入不得绕过()
        {
            // 40 个汉字 = 40 字符（< 64）但 120 字节（> 64）：必须拒
            string wide = new string('中', 40);
            Assert.True(wide.Length < FieldLimits.BusinessKeyBytes);
            Assert.False(FieldValidation.IsValid(wide, FieldLimits.BusinessKeyBytes));

            // 恰好达到字节上限则放行（边界值不误杀）
            string exact = new string('中', FieldLimits.BusinessKeyBytes / 3);   // 21×3 = 63 字节 ≤ 64
            Assert.True(FieldValidation.IsValid(exact, FieldLimits.BusinessKeyBytes));
        }

        [Fact]
        public void 空与空白_都算不合法()
        {
            Assert.False(FieldValidation.IsValid(null, FieldLimits.ShortIdBytes));
            Assert.False(FieldValidation.IsValid(string.Empty, FieldLimits.ShortIdBytes));
            Assert.False(FieldValidation.IsValid("   ", FieldLimits.ShortIdBytes));   // 空白不是有效标识
            Assert.True(FieldValidation.IsValid("a", FieldLimits.ShortIdBytes));
        }

        [Fact]
        public void 错误文案只回显字段名与上限_不回显字段值()
        {
            var errors = new List<string>();

            Assert.False(FieldValidation.Append(errors, "requestId", "   ", FieldLimits.ShortIdBytes));
            Assert.False(FieldValidation.Append(errors, "matchId", new string('x', 65), FieldLimits.BusinessKeyBytes));
            Assert.True(FieldValidation.Append(errors, "playerId", "p1", FieldLimits.BusinessKeyBytes));

            Assert.Equal(2, errors.Count);                    // 一次报全（合法项不产生错误）
            Assert.Contains("requestId 不得为空", errors);
            Assert.Contains("matchId 不得超过 64 UTF-8 字节", errors);
            Assert.DoesNotContain(errors, e => e.Contains(new string('x', 65)));   // 字段值不进错误体
        }

        [Fact]
        public void 上限表_按语义类别取值()
        {
            Assert.Equal(128, FieldLimits.ShortIdBytes);        // requestId/deviceId/accountId
            Assert.Equal(64, FieldLimits.BusinessKeyBytes);     // matchId/playerId/instanceId/roomId/buildHash
            Assert.Equal(128, FieldLimits.AddressBytes);        // host:port
            Assert.Equal(256, FieldLimits.OperationIdBytes);    // operationId
        }
    }
}
