using System.Collections.Generic;
using DynamicData.Binding;

namespace DynamicData.Tests.Cache
{

    public class DataElement<T> : AbstractNotifyPropertyChanged
    {
        public string ItemName { get; }
        public string CaptureName { get; }

        public (string itemName, string captureName) Key => (ItemName, CaptureName);

        private T _value;

        public T Value
        {
            get => _value;
            set => SetAndRaise(ref _value, value);
        }

        public DataElement(string itemName, string captureName, T value)
        {
            ItemName = itemName;
            CaptureName = captureName;
            _value = value;
        }

        public override string ToString()
        {
            return $"Key: ({ItemName}-{CaptureName}). Value: {Value}";
        }
    }


    public static class Data
    {
        public static IEnumerable<DataElement<string>> Labels()
        {
            yield return (new DataElement<string>("W1", "_", "J1"));
            yield return (new DataElement<string>("W2", "_", "J1"));
            yield return (new DataElement<string>("W3", "_", "J2"));
            yield return (new DataElement<string>("W4", "_", "J2"));
        }

        public static IEnumerable<DataElement<double>> Values()
        {
            yield return (new DataElement<double>("W1", "CapA", 1.0));
            yield return (new DataElement<double>("W2", "CapA", 1.0));
            yield return (new DataElement<double>("W3", "CapA", 1.0));
            yield return (new DataElement<double>("W4", "CapA", 1.0));
            yield return (new DataElement<double>("W1", "CapB", 1.0));
            yield return (new DataElement<double>("W2", "CapB", 1.0));
            yield return (new DataElement<double>("W3", "CapB", 1.0));
            yield return (new DataElement<double>("W4", "CapB", 1.0));
            yield return (new DataElement<double>("W1", "CapC", 1.0));
            yield return (new DataElement<double>("W2", "CapC", 1.0));
            yield return (new DataElement<double>("W3", "CapC", 1.0));
            yield return (new DataElement<double>("W4", "CapC", 1.0));
        }

        /// <summary>
        /// These are the expected values once both caches are populated with data
        /// </summary>
        /// <returns></returns>
        public static Dictionary<(string, string), double> ExpectedInitial()
        {
            var dict = new Dictionary<(string, string), double>
            {
                [("J1", "CapA")] = 2.0,
                [("J2", "CapA")] = 2.0,
                [("J1", "CapB")] = 2.0,
                [("J2", "CapB")] = 2.0,
                [("J1", "CapC")] = 2.0,
                [("J2", "CapC")] = 2.0
            };

            return dict;
        }

        /// <summary>
        /// These are the expected values once position 3 label is changed from J2 to J1
        /// </summary>
        /// <returns></returns>
        public static Dictionary<(string, string), double> ExpectedSecond()
        {
            var dict = new Dictionary<(string, string), double>
            {
                [("J1", "CapA")] = 3.0,
                [("J2", "CapA")] = 1.0,
                [("J1", "CapB")] = 3.0,
                [("J2", "CapB")] = 1.0,
                [("J1", "CapC")] = 3.0,
                [("J2", "CapC")] = 1.0
            };

            return dict;
        }

        /// <summary>
        /// These are the expected values once position 4 label is changed from J2 to J1
        /// </summary>
        /// <returns></returns>
        public static Dictionary<(string, string), double> ExpectedThird()
        {
            var dict = new Dictionary<(string, string), double>
            {
                [("J1", "CapA")] = 4.0,
                [("J1", "CapB")] = 4.0,
                [("J1", "CapC")] = 4.0
            };

            return dict;
        }

        public static Dictionary<(string, string), double> ExpectedFourth()
        {
            var dict = new Dictionary<(string, string), double>
            {
                [("J1", "CapA")] = 3.0,
                [("X", "CapA")] = 1.0, //a new label, it is applied across each capture group
                [("J1", "CapB")] = 3.0,
                [("X", "CapB")] = 1.0,
                [("J1", "CapC")] = 3.0,
                [("X", "CapC")] = 1.0
            };

            return dict;
        }

        public static Dictionary<(string, string), double> ExpectedFifth()
        {
            var dict = new Dictionary<(string, string), double>
            {
                [("J1", "CapA")] = 3.0,
                [("X", "CapA")] = 1.0,
                [("J1", "CapB")] = 3.0,
                [("X", "CapB")] = 1.0,
                [("J1", "CapC")] = 3.0,
                [("X", "CapC")] = 1.0,
                [("J1", "CapD")] = 3.0, //a new capture here
                [("X", "CapD")] = 1.0 //a new capture here
            };

            return dict;
        }
    }
}