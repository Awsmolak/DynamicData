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
            yield return (new DataElement<string>("1", "_", "J1"));
            yield return (new DataElement<string>("2", "_", "J1"));
            yield return (new DataElement<string>("3", "_", "J2"));
            yield return (new DataElement<string>("4", "_", "J2"));
        }

        public static IEnumerable<DataElement<double>> Values()
        {
            yield return (new DataElement<double>("1", "A", 1.0));
            yield return (new DataElement<double>("2", "A", 1.0));
            yield return (new DataElement<double>("3", "A", 1.0));
            yield return (new DataElement<double>("4", "A", 1.0));
            yield return (new DataElement<double>("1", "B", 1.0));
            yield return (new DataElement<double>("2", "B", 1.0));
            yield return (new DataElement<double>("3", "B", 1.0));
            yield return (new DataElement<double>("4", "B", 1.0));
            yield return (new DataElement<double>("1", "C", 1.0));
            yield return (new DataElement<double>("2", "C", 1.0));
            yield return (new DataElement<double>("3", "C", 1.0));
            yield return (new DataElement<double>("4", "C", 1.0));
        }

        /// <summary>
        /// These are the expected values once both caches are populated with data
        /// </summary>
        /// <returns></returns>
        public static Dictionary<(string, string), double> ExpectedInitial()
        {
            var dict = new Dictionary<(string, string), double>
            {
                [("J1", "A")] = 2.0,
                [("J2", "A")] = 2.0,
                [("J1", "B")] = 2.0,
                [("J2", "B")] = 2.0,
                [("J1", "C")] = 2.0,
                [("J2", "C")] = 2.0
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
                [("J1", "A")] = 3.0,
                [("J2", "A")] = 1.0,
                [("J1", "B")] = 3.0,
                [("J2", "B")] = 1.0,
                [("J1", "C")] = 3.0,
                [("J2", "C")] = 1.0
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
                [("J1", "A")] = 4.0,
                [("J1", "B")] = 4.0,
                [("J1", "C")] = 4.0
            };

            return dict;
        }

        public static Dictionary<(string, string), double> ExpectedFourth()
        {
            var dict = new Dictionary<(string, string), double>
            {
                [("J1", "A")] = 3.0,
                [("X", "A")] = 1.0, //a new label, it is applied across each capture group
                [("J1", "B")] = 3.0,
                [("X", "B")] = 1.0,
                [("J1", "C")] = 3.0,
                [("X", "C")] = 1.0
            };

            return dict;
        }

        public static Dictionary<(string, string), double> ExpectedFifth()
        {
            var dict = new Dictionary<(string, string), double>
            {
                [("J1", "A")] = 3.0,
                [("X", "A")] = 1.0,
                [("J1", "B")] = 3.0,
                [("X", "B")] = 1.0,
                [("J1", "C")] = 3.0,
                [("X", "C")] = 1.0,
                [("J1", "D")] = 3.0, //a new capture here
                [("X", "D")] = 1.0 //a new capture here
            };

            return dict;
        }
    }
}