using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;

namespace Telepathy
{
    public class X509Certificate2Builder
    {
        public string SubjectName
        { set { _subjectName = value; } }

        public string IssuerName
        { set { _issuerName = value; } }

        public AsymmetricAlgorithm IssuerPrivateKey
        { set { _issuerPrivateKey = value; } }

        public X509Certificate2 Issuer
        {
            set
            {
                _issuer = value;
                _issuerName = value.IssuerName.Name;
                if (value.HasPrivateKey)
                    _issuerPrivateKey = value.PrivateKey;
            }
        }

        public int? KeyStrength
        { set { _keyStrength = value ?? 2048; } }

        public DateTime? NotBefore
        { set { _notBefore = value; } }

        public DateTime? NotAfter
        { set { _notAfter = value; } }

        public bool Intermediate
        { set { _intermediate = value; } }

        private string _subjectName;
        private X509Certificate2 _issuer;
        private string _issuerName;
        private AsymmetricAlgorithm _issuerPrivateKey;
        private int _keyStrength = 2048;
        private DateTime? _notBefore;
        private DateTime? _notAfter;
        private bool _intermediate = true;

        public X509Certificate2 Build()
        {
            // Generating Random Numbers
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            // The Certificate Generator
            var certificateGenerator = new X509V3CertificateGenerator();

            // Serial Number
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Issuer and Subject Name
            certificateGenerator.SetIssuerDN(new X509Name(_issuerName ?? _subjectName));
            certificateGenerator.SetSubjectDN(new X509Name(_subjectName));

            // Authority Key Identifier
            if (_issuer != null)
            {
                var authorityKeyIdentifier = new AuthorityKeyIdentifierStructure(
                    DotNetUtilities.FromX509Certificate(_issuer));
                certificateGenerator.AddExtension(
                    X509Extensions.AuthorityKeyIdentifier.Id, false, authorityKeyIdentifier);
            }

            // Basic Constraints - certificate is allowed to be used as intermediate.
            certificateGenerator.AddExtension(
                X509Extensions.BasicConstraints.Id, true, new BasicConstraints(_intermediate));

            // Valid For
            certificateGenerator.SetNotBefore(_notBefore ?? DateTime.UtcNow.Date);
            certificateGenerator.SetNotAfter(_notAfter ?? DateTime.UtcNow.Date.AddYears(2));

            // Subject Public Key
            var keyGenerationParameters = new KeyGenerationParameters(random, _keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);

            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();
            var issuerKeyPair = _issuerPrivateKey == null
                ? subjectKeyPair
                : DotNetUtilities.GetKeyPair(_issuerPrivateKey);

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            // Signature Algorithm
            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA512WITHRSA", issuerKeyPair.Private, random);

            // selfsign certificate
            var certificate = certificateGenerator.Generate(signatureFactory);

            // Merge into X509Certificate2
            return new X509Certificate2(certificate.GetEncoded())
            {
                PrivateKey = ConvertToRsaPrivateKey(subjectKeyPair)
            };
        }

        private static AsymmetricAlgorithm ConvertToRsaPrivateKey(AsymmetricCipherKeyPair keyPair)
        {
            var keyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private);
            var seq = (Asn1Sequence)Asn1Object.FromByteArray(keyInfo.ParsePrivateKey().GetDerEncoded());
            if (seq.Count != 9)
                throw new PemException("malformed sequence in RSA private key");

            var rsa = RsaPrivateKeyStructure.GetInstance(seq);
            var rsaparams = new RsaPrivateCrtKeyParameters(
                rsa.Modulus, rsa.PublicExponent, rsa.PrivateExponent, rsa.Prime1, rsa.Prime2, rsa.Exponent1,
                rsa.Exponent2, rsa.Coefficient);

            return DotNetUtilities.ToRSA(rsaparams);
        }
    }
}