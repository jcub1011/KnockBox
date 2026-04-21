using KnockBox.Services.Logic.Admin;

namespace KnockBox.Tests.Unit.Services.Logic.Admin
{
    [TestClass]
    public sealed class AdminPasswordValidatorTests
    {
        private static string? Run(string? newPw, string? confirmPw, string currentPw = "changeme") =>
            AdminPasswordValidator.Validate(newPw, confirmPw, pw => pw == currentPw);

        [TestMethod]
        public void Null_NewPassword_ReturnsEmptyError()
        {
            Assert.AreEqual("Password cannot be empty.", Run(null, "ignored"));
        }

        [TestMethod]
        public void Whitespace_NewPassword_ReturnsEmptyError()
        {
            Assert.AreEqual("Password cannot be empty.", Run("   ", "   "));
        }

        [TestMethod]
        public void ReusingCurrentPassword_ReturnsReuseError()
        {
            Assert.AreEqual(
                "You cannot reuse your current password.",
                Run("changeme", "changeme", currentPw: "changeme"));
        }

        [TestMethod]
        public void MismatchedConfirm_ReturnsMismatchError()
        {
            Assert.AreEqual("Passwords do not match.", Run("newpassword1", "newpassword2"));
        }

        [TestMethod]
        public void TooShort_ReturnsLengthError()
        {
            Assert.AreEqual(
                "Password must be at least 8 characters long.",
                Run("short1", "short1"));
        }

        [TestMethod]
        public void ValidPassword_ReturnsNull()
        {
            Assert.IsNull(Run("abcdefgh", "abcdefgh"));
        }

        [TestMethod]
        public void OrderingCheck_ReuseBeatsMismatch()
        {
            // User types current password as "new" and a typo as confirm — the reuse error
            // is the more informative message and must win over the mismatch error.
            Assert.AreEqual(
                "You cannot reuse your current password.",
                Run("changeme", "changemx", currentPw: "changeme"));
        }

        [TestMethod]
        public void OrderingCheck_MismatchBeatsLength()
        {
            // Short passwords that don't match should report mismatch first — length is
            // only worth complaining about once the user has agreed on a password.
            Assert.AreEqual("Passwords do not match.", Run("abc", "abcd"));
        }
    }
}
