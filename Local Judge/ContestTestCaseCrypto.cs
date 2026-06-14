using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Local_Judge
{
    public static class ContestTestCaseCrypto
    {
        public const string Scheme = "contest-testcases-aes-gcm-v1";
        public const string Kdf = "pbkdf2-sha256";

        private const int Iterations = 100_000;
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;

        private static readonly Regex PinRegex = new(@"^\d{4}$", RegexOptions.Compiled);

        public static bool IsValidPin(string? pin)
        {
            return PinRegex.IsMatch(pin ?? string.Empty);
        }

        public static EncryptedTestCasesDocument Encrypt(
            IReadOnlyList<TestCaseDocument> testCases,
            string pin,
            JsonSerializerOptions jsonOptions)
        {
            if (!IsValidPin(pin))
            {
                throw new ArgumentException("대회 채점 테스트케이스 암호는 4자리 숫자여야 합니다.", nameof(pin));
            }

            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(testCases ?? Array.Empty<TestCaseDocument>(), jsonOptions);
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] key = DeriveKey(pin, salt);
            byte[] cipherText = new byte[plaintext.Length];
            byte[] tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, cipherText, tag);
            }

            byte[] payload = new byte[cipherText.Length + tag.Length];
            Buffer.BlockCopy(cipherText, 0, payload, 0, cipherText.Length);
            Buffer.BlockCopy(tag, 0, payload, cipherText.Length, tag.Length);
            CryptographicOperations.ZeroMemory(key);

            return new EncryptedTestCasesDocument
            {
                Scheme = Scheme,
                Kdf = Kdf,
                Iterations = Iterations,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                CipherText = Convert.ToBase64String(payload)
            };
        }

        public static bool TryDecrypt(
            EncryptedTestCasesDocument document,
            string? pin,
            JsonSerializerOptions jsonOptions,
            out List<TestCaseDocument> testCases)
        {
            testCases = new List<TestCaseDocument>();
            if (!IsValidPin(pin)
                || !string.Equals(document.Scheme, Scheme, StringComparison.Ordinal)
                || !string.Equals(document.Kdf, Kdf, StringComparison.Ordinal)
                || document.Iterations != Iterations)
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(document.Salt);
                byte[] nonce = Convert.FromBase64String(document.Nonce);
                byte[] payload = Convert.FromBase64String(document.CipherText);
                if (salt.Length != SaltSize || nonce.Length != NonceSize || payload.Length < TagSize)
                {
                    return false;
                }

                int cipherTextLength = payload.Length - TagSize;
                byte[] cipherText = new byte[cipherTextLength];
                byte[] tag = new byte[TagSize];
                Buffer.BlockCopy(payload, 0, cipherText, 0, cipherTextLength);
                Buffer.BlockCopy(payload, cipherTextLength, tag, 0, TagSize);

                byte[] plaintext = new byte[cipherTextLength];
                byte[] key = DeriveKey(pin!, salt);
                using (var aes = new AesGcm(key, TagSize))
                {
                    aes.Decrypt(nonce, cipherText, tag, plaintext);
                }

                CryptographicOperations.ZeroMemory(key);
                testCases = JsonSerializer.Deserialize<List<TestCaseDocument>>(plaintext, jsonOptions)
                    ?? new List<TestCaseDocument>();
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static byte[] DeriveKey(string pin, byte[] salt)
        {
            using var deriveBytes = new Rfc2898DeriveBytes(pin, salt, Iterations, HashAlgorithmName.SHA256);
            return deriveBytes.GetBytes(KeySize);
        }
    }
}
