using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer;

namespace Ripple.Tests
{
    [TestClass]
    public class CompatibilityTests
    {
        [TestMethod]
        public void ExistingTransactionsKeepTheirExactSignedBytes()
        {
            var fixtures = JArray.Parse(File.ReadAllText(Fixture("legacy-transactions.json")));
            foreach (JObject fixture in fixtures)
            {
                var tx = (JObject)fixture["transaction"];
                string before = tx.ToString();
                string blob = RippleClient.SignTransaction(tx);
                Assert.AreEqual((string)fixture["tx_blob"], blob, (string)fixture["name"]);
                Assert.AreEqual(before, tx.ToString(), "Signing must not modify the caller's JSON.");
                Assert.AreEqual(blob, StObject.FromHex(blob).ToHex(), (string)fixture["name"]);
            }
        }

        [TestMethod]
        public void ExistingPublicTypesAndMembersRemainAvailable()
        {
            var current = new HashSet<string>(DescribeApi(typeof(RippleClient).Assembly), StringComparer.Ordinal);
#if NET472
            const string baseline = "legacy-public-api-net472.txt";
#else
            const string baseline = "legacy-public-api.txt";
#endif
            string[] missing = File.ReadAllLines(Fixture(baseline)).Where(line => !current.Contains(line)).ToArray();
            Assert.AreEqual(0, missing.Length, string.Join(Environment.NewLine, missing.Take(15)));
        }

        [DataTestMethod]
        [DataRow(TestTransactions.SecpSeed, TestTransactions.Account,
            "030D58EB48B4420B1F7B9DF55087E0E29FEF0E8468F9A6825B01CA2C361042D435",
            "30440220583A91C95E54E6A651C47BEC22744E0B101E2C4060E7B08F6341657DAD9BC3EE02207D1489C7395DB0188D3A56A977ECBA54B36FA9371B40319655B1B4429E33EF2D")]
        [DataRow(TestTransactions.EdSeed, TestTransactions.Destination,
            "ED01FA53FA5A7E77798F882ECE20B1ABC00BB358A9E55A202D0D0676BD0CE37A63",
            "CB199E1BFD4E3DAA105E4832EEDFA36413E1F44205E4EFB9E27E826044C21E3E2E848BBC8195E8959BADF887599B7310AD1B7047EF11B682E0D068F73749750E")]
        public void KeysAndSignaturesMatchOfficialVectors(string seed, string address, string publicKey, string signature)
        {
            var key = Seed.FromBase58(seed).KeyPair();
            byte[] message = Encoding.UTF8.GetBytes("test message");
            Assert.AreEqual(address, key.Id());
            Assert.AreEqual(publicKey, B16.Encode(key.CanonicalPubBytes()));
            byte[] signed = key.Sign(message);
            Assert.AreEqual(signature, B16.Encode(signed));
            Assert.IsTrue(key.Verify(message, signed));
            message[0] ^= 1;
            Assert.IsFalse(key.Verify(message, signed));
        }

        [TestMethod]
        public void GeneratedAddressesRemainValidAndMatchTheirSeeds()
        {
            var addresses = new HashSet<string>();
            for (int i = 0; i < 10; i++)
            {
                AddressData address = RippleClient.GenerateAddress();
                Assert.IsTrue(RippleClient.ValidateAddress(address.Address));
                Assert.AreEqual(address.Address, Seed.FromBase58(address.Seed).KeyPair().Id());
                Assert.IsTrue(addresses.Add(address.Address));
            }
        }

        private static string Fixture(string name) => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);

        private static string TypeName(Type type)
        {
            return type.ToString() + (type.Namespace != null && type.Namespace.StartsWith("Org.BouncyCastle", StringComparison.Ordinal)
                ? " [" + type.Assembly.GetName().Name + "]" : "");
        }

        private static string Parameters(ParameterInfo[] parameters)
        {
            return string.Join(";", parameters.Select(p => p.Name + ":" + TypeName(p.ParameterType) + ":" + p.Attributes + ":" + p.DefaultValue));
        }

        private static IEnumerable<string> DescribeApi(Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Type type in assembly.GetExportedTypes())
            {
                yield return "TYPE " + type.FullName + " BASE " + (type.BaseType == null ? "" : TypeName(type.BaseType)) + " INTERFACES " +
                    string.Join(";", type.GetInterfaces().Select(TypeName).OrderBy(x => x, StringComparer.Ordinal));
                foreach (ConstructorInfo ctor in type.GetConstructors(flags))
                    yield return type.FullName + " CTOR " + ctor.Attributes + " " + Parameters(ctor.GetParameters());
                foreach (MethodInfo method in type.GetMethods(flags))
                    yield return type.FullName + " METHOD " + method.Name + " " + method.Attributes + " " + TypeName(method.ReturnType) + " (" + Parameters(method.GetParameters()) + ")";
                foreach (FieldInfo field in type.GetFields(flags))
                    yield return type.FullName + " FIELD " + field.Name + " " + field.Attributes + " " + TypeName(field.FieldType);
            }
        }
    }
}
