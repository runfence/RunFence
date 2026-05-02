using RunFence.Account;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="PasswordHelper.GenerateRandomPassword"/>: category coverage,
/// length bounds, and correctness under extended load.
/// </summary>
public class PasswordHelperTests
{
    [Fact]
    public void GenerateRandomPassword_AlwaysProducesValidPassword()
    {
        // Verify across 500 iterations that every generated password:
        //   - is 15–20 characters long (per the random length range)
        //   - contains at least one upper-case letter
        //   - contains at least one lower-case letter
        //   - contains at least one digit
        //   - contains at least one symbol
        //
        // The two-pass EnsureCategory loop guarantees all categories even when a
        // first-pass insertion clobbers the sole representative of another category.
        // Running 500 iterations provides high statistical confidence that the logic
        // is correct under adversarial random seeds.
        for (int i = 0; i < 500; i++)
        {
            var pwd = PasswordHelper.GenerateRandomPassword();
            Assert.InRange(pwd.Length, 15, 20);
            Assert.Contains(pwd, c => char.IsUpper(c));
            Assert.Contains(pwd, c => char.IsLower(c));
            Assert.Contains(pwd, c => char.IsDigit(c));
            Assert.Contains(pwd, c => !char.IsLetterOrDigit(c));
        }
    }
}
