using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using DynamicData.Binding;
using DynamicData.Cache;
using DynamicData.Cache.Internal;
using DynamicData.Kernel;
using FluentAssertions;
using Xunit;

namespace DynamicData.Tests.Cache
{
    public class GroupThenJoinFixture : IDisposable
    {
        private readonly SourceCache<DataElement<double>, (string, string)> _valuesSource;
        private readonly SourceCache<DataElement<string>, (string, string)> _joinLabelsSource;

        public GroupThenJoinFixture()
        {
            _valuesSource = new SourceCache<DataElement<double>, (string, string)>(e => e.Key);
            _joinLabelsSource = new SourceCache<DataElement<string>, (string, string)>(e => e.Key);
        }

        [Fact]
        public void CanJoinInitial()
        {
            var input1 = _valuesSource.AsObservableCache().Connect();
            var input2 = _joinLabelsSource.AsObservableCache().Connect().ChangeKey(x => x.ItemName);

            var captureGroups = input1.GroupOnProperty(x => x.CaptureName)
                .Transform(g => (g.Cache, g.Key), true).AsObservableCache();

            var capGroupsWithReps = captureGroups.Connect().Transform(group =>
            {
                //In order to join two caches key to just the item.
                var input1Cache = group.Cache.Connect().ChangeKey(k => k.ItemName).AsObservableCache();

                //Associate the rep group with the element
                var joined = input1Cache.Connect().AutoRefresh()
                    .LeftJoin(input2.AsObservableCache().Connect().AutoRefresh(), w => w.ItemName, (s, element, repGroupOpt) =>
                    {

                        //if no replicate group associated with en element item, output with it's own item name so it can be reflected on the output
                        if (repGroupOpt.HasValue)
                        {
                            var groupName = (string)repGroupOpt.Value.Value;
                            Debug.WriteLine("JOIN APPLIED - MATCH " + groupName);
                            return (element, (string)repGroupOpt.Value.Value);
                        }
                        else
                        {
                            Debug.WriteLine("JOIN APPLIED - NO MATCH");
                            return (element, element.ItemName);
                        }

                    }).AsObservableCache();


                return (joined, group.Key);

            }, true).AutoRefreshOnObservable(x => input2.AsObservableCache().Connect());

            var results = capGroupsWithReps.AsAggregator();

            LoadValues();

            Debug.WriteLine("");

            //Three outer capture groups A, B, C
            results.Data.Count.Should().Be(3);
            var joinResult = results.Data.Items.First().joined.Items.First();

            //after loading elements, initial join uses default value since no matching key in right 
            joinResult.Item2.Should().Be(joinResult.element.ItemName);

            LoadLabels();

            var joinedLabels = results.Data.Items.First().joined.Items.Select(i => i.Item2);
            //after loading labels, join should result in 
            Debug.WriteLine("");

            joinedLabels.Should().Equal("J1", "J1", "J2", "J2", "J3", "J3");
            Debug.WriteLine("");

            _joinLabelsSource.AddOrUpdate(new DataElement<string>("3", "_", "J1"));

            joinedLabels = results.Data.Items.First().joined.Items.Select(i => i.Item2);

            //this is showing that the join does, in fact update...
            joinedLabels.Should().Equal("J1", "J1", "J1", "J2", "J3", "J3");
        }

        [Fact]
        public void CanRecombine()
        {
            var input1 = _valuesSource.Connect();
            var input2 = _joinLabelsSource.Connect().ChangeKey(x => x.ItemName);

            var captureGroups = input1.GroupOnProperty(x => x.CaptureName)
                .Transform(g => (g.Cache, g.Key), true);

            var capGroupsWithReps = captureGroups.Transform(group =>
            {
                //In order to join two caches key to just the item.
                var input1Cache = group.Cache.Connect().ChangeKey(k => k.ItemName);

                //Associate the rep group with the element
                var joined = input1Cache
                    .LeftJoin(input2, w => w.ItemName, (s, element, repGroupOpt) =>
                    {

                        //if no replicate group associated with en element item, output with it's own item name so it can be reflected on the output
                        if (repGroupOpt.HasValue)
                        {
                            var groupName = (string)repGroupOpt.Value.Value;
                            Debug.WriteLine("JOIN APPLIED - MATCH " + groupName);
                            return (element, (string)repGroupOpt.Value.Value);
                        }
                        else
                        {
                            Debug.WriteLine("JOIN APPLIED - NO MATCH");
                            return (element, element.ItemName);
                        }
                    });


                return (joined, group.Key);

            }, true); // NOTE: does this one need to be here for the group add/remove? .AutoRefreshOnObservable(x => input2);

            var groupedOnRep = capGroupsWithReps
                .AutoRefreshOnObservable(x => x.joined)//NOTE: This AROE must be here for things to work
                .Transform(g =>
                {
                    //group the elements based upon the results of the joined labels and transform to new element which is a combination of the element values
                    var combinedReps = g.joined.Group(s => s.Item2).Transform(t =>
                    {
                        var val = t.Cache.Items.Select(i => i.element.Value).Sum();
                        Debug.WriteLine($"New combined element {t.Key} Count: {val}");
                        return new DataElement<double>((string)t.Key, g.Key, val);
                    }, true).ChangeKey(k => k.Key);

                    return combinedReps;
                }, true);

            //originally was trying to use mergemany to join all the elements back together, but removed items were not reflected in resulting cache
            var combined = groupedOnRep.UnionMany(x => x);


            var results = combined.AsAggregator();

            Debug.WriteLine("");

            LoadValues();
            LoadLabels();

            Debug.WriteLine("");

            var element = results.Data.Lookup(("J1", "A")).Value;

            //should be the sum of 2 values
            element.Value.Should().Be(2);

            CheckAgainstExpected(results.Data, ExpectedInitial());

            _joinLabelsSource.AddOrUpdate(new DataElement<string>("3", "_", "J1"));

            Debug.WriteLine("");

            element = results.Data.Lookup(("J1", "A")).Value;

            //should be the sum of 3 values
            element.Value.Should().Be(3);

            CheckAgainstExpected(results.Data, ExpectedSecond());

            //check reducing down to one label
            _joinLabelsSource.AddOrUpdate(new DataElement<string>("4", "_", "J1"));

            CheckAgainstExpected(results.Data, ExpectedThird());

            //check adding new label
            _joinLabelsSource.AddOrUpdate(new DataElement<string>("4", "_", "X"));

            CheckAgainstExpected(results.Data, ExpectedFourth());

            //check adding new capture
            AddCapture("D");
            CheckAgainstExpected(results.Data, ExpectedFifth());

            RemoveCapture("D");
            CheckAgainstExpected(results.Data, ExpectedFourth());
        }

        [Fact]
        public void ThisDeadlocks()
        {
            var input1 = _valuesSource.Connect();
            var input2 = _joinLabelsSource.Connect().ChangeKey(x => x.ItemName);

            var captureGroups = input1.GroupOnProperty(x => x.CaptureName)
                .Transform(g => (g.Cache, g.Key), true);

            var capGroupsWithReps = captureGroups.Transform(group =>
            {
                //In order to join two caches key to just the item.
                var input1Cache = group.Cache.Connect().ChangeKey(k => k.ItemName);

                //Associate the rep group with the element
                var joined = input1Cache
                    .LeftJoin(input2, w => w.ItemName, (s, element, repGroupOpt) =>
                    {

                        //if no replicate group associated with en element item, output with it's own item name so it can be reflected on the output
                        if (repGroupOpt.HasValue)
                        {
                            var groupName = (string)repGroupOpt.Value.Value;
                            Debug.WriteLine("JOIN APPLIED - MATCH " + groupName);
                            return (element, (string)repGroupOpt.Value.Value);
                        }
                        else
                        {
                            Debug.WriteLine("JOIN APPLIED - NO MATCH");
                            return (element, element.ItemName);
                        }
                    });


                return (joined, group.Key);

            }, true); // NOTE: does this one need to be here for the group add/remove? .AutoRefreshOnObservable(x => input2);

            var groupedOnRep = capGroupsWithReps
                .AutoRefreshOnObservable(x => x.joined, changeSetBuffer:TimeSpan.FromMilliseconds(10))//NOTE: This AROE must be here for things to work properly
                .Transform(g =>
                {
                    //group the elements based upon the results of the joined labels and transform to new element which is a combination of the element values
                    var combinedReps = g.joined.Group(s => s.Item2).Transform(t =>
                    {
                        var val = t.Cache.Items.Select(i => i.element.Value).Sum();
                        Debug.WriteLine($"New combined element {t.Key} Count: {val}");
                        return new DataElement<double>((string)t.Key, g.Key, val);
                    }, true).ChangeKey(k => k.Key);

                    return combinedReps;
                }, true);

            //originally was trying to use mergemany to join all the elements back together, but removed items were not reflected in resulting cache
            var combined = groupedOnRep.UnionMany(x => x);


            var results = combined.AsAggregator();

            Debug.WriteLine("");

            LoadValues();
            LoadLabels();

            Debug.WriteLine("");

            var element = results.Data.Lookup(("J1", "A")).Value;

            //should be the sum of 2 values
            element.Value.Should().Be(2);

            _joinLabelsSource.AddOrUpdate(new DataElement<string>("3", "_", "J1"));

            Debug.WriteLine("");

            element = results.Data.Lookup(("J1", "A")).Value;

            //should be the sum of 3 values
            element.Value.Should().Be(3);
        }

        private void LoadValues()
        {
            _valuesSource.Edit(inner =>
            {
                inner.AddOrUpdate(new DataElement<double>("1", "A", 1.0));
                inner.AddOrUpdate(new DataElement<double>("2", "A", 1.0));
                inner.AddOrUpdate(new DataElement<double>("3", "A", 1.0));
                inner.AddOrUpdate(new DataElement<double>("4", "A", 1.0));
                inner.AddOrUpdate(new DataElement<double>("1", "B", 1.0));
                inner.AddOrUpdate(new DataElement<double>("2", "B", 1.0));
                inner.AddOrUpdate(new DataElement<double>("3", "B", 1.0));
                inner.AddOrUpdate(new DataElement<double>("4", "B", 1.0));
                inner.AddOrUpdate(new DataElement<double>("1", "C", 1.0));
                inner.AddOrUpdate(new DataElement<double>("2", "C", 1.0));
                inner.AddOrUpdate(new DataElement<double>("3", "C", 1.0));
                inner.AddOrUpdate(new DataElement<double>("4", "C", 1.0));
            });
        }

        private void AddCapture(string captureString)
        {
            _valuesSource.Edit(inner =>
            {
                //note: the "matrix" is never "jagged", my application guarantees rectangularity
                inner.AddOrUpdate(new DataElement<double>("1", captureString, 1.0));
                inner.AddOrUpdate(new DataElement<double>("2", captureString, 1.0));
                inner.AddOrUpdate(new DataElement<double>("3", captureString, 1.0));
                inner.AddOrUpdate(new DataElement<double>("4", captureString, 1.0));
            });
        }

        private void RemoveCapture(string captureString)
        {
            _valuesSource.Edit(inner =>
            {
                inner.Remove(("1",captureString));
                inner.Remove(("2", captureString));
                inner.Remove(("3", captureString));
                inner.Remove(("4", captureString));
            });
        }

        private void LoadLabels()
        {
            _joinLabelsSource.Edit(inner =>
            {
                inner.AddOrUpdate(new DataElement<string>("1", "_", "J1"));
                inner.AddOrUpdate(new DataElement<string>("2", "_", "J1"));
                inner.AddOrUpdate(new DataElement<string>("3", "_", "J2"));
                inner.AddOrUpdate(new DataElement<string>("4", "_", "J2"));
            });
        }

        private void CheckAgainstExpected(IObservableCache<DataElement<double>, (string itemName, string captureName)> result, Dictionary<(string, string), double> expected)
        {
            foreach (var e in expected)
            {
                result.Lookup(e.Key).Value.Value.Should().Be(e.Value);
            }

            result.Count.Should().Be(expected.Count);
        }
        
        /// <summary>
        /// These are the expected values once both caches are populated with data
        /// </summary>
        /// <returns></returns>
        private Dictionary<(string, string), double> ExpectedInitial()
        {
            var dict = new Dictionary<(string, string), double>();

            dict[("J1", "A")] = 2.0;
            dict[("J2", "A")] = 2.0;
            dict[("J1", "B")] = 2.0;
            dict[("J2", "B")] = 2.0;
            dict[("J1", "C")] = 2.0;
            dict[("J2", "C")] = 2.0;

            return dict;
        }


        /// <summary>
        /// These are the expected values once position 3 label is changed from J2 to J1
        /// </summary>
        /// <returns></returns>
        private Dictionary<(string, string), double> ExpectedSecond()
        {
            var dict = new Dictionary<(string, string), double>();

            dict[("J1", "A")] = 3.0;
            dict[("J2", "A")] = 1.0;
            dict[("J1", "B")] = 3.0;
            dict[("J2", "B")] = 1.0;
            dict[("J1", "C")] = 3.0;
            dict[("J2", "C")] = 1.0;

            return dict;
        }

        /// <summary>
        /// These are the expected values once position 4 label is changed from J2 to J1
        /// </summary>
        /// <returns></returns>
        private Dictionary<(string, string), double> ExpectedThird()
        {
            var dict = new Dictionary<(string, string), double>();

            dict[("J1", "A")] = 4.0;
            dict[("J1", "B")] = 4.0;
            dict[("J1", "C")] = 4.0;

            return dict;
        }

        private Dictionary<(string, string), double> ExpectedFourth()
        {
            var dict = new Dictionary<(string, string), double>();

            dict[("J1", "A")] = 3.0;
            dict[("X", "A")] = 1.0; //a new label, it is applied across each capture group
            dict[("J1", "B")] = 3.0;
            dict[("X", "B")] = 1.0;
            dict[("J1", "C")] = 3.0;
            dict[("X", "C")] = 1.0;

            return dict;
        }

        private Dictionary<(string, string), double> ExpectedFifth()
        {
            var dict = new Dictionary<(string, string), double>();

            dict[("J1", "A")] = 3.0;
            dict[("X", "A")] = 1.0;
            dict[("J1", "B")] = 3.0;
            dict[("X", "B")] = 1.0;
            dict[("J1", "C")] = 3.0;
            dict[("X", "C")] = 1.0;
            dict[("J1", "D")] = 3.0; //a new capture here
            dict[("X", "D")] = 1.0; //a new capture here

            return dict;
        }


        public void Dispose()
        {
            _valuesSource.Dispose();
            _joinLabelsSource.Dispose();
        }
    }

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
    }

    public static class DynamicDataExtensions
    {
        public static IObservable<IChangeSet<TObjectOut, TKeyOut>> UnionMany<TObjectOut, TKeyOut, TObjectIn, TKeyIn>(
            this IObservable<IChangeSet<TObjectIn, TKeyIn>> source, Func<TObjectIn, IObservable<IChangeSet<TObjectOut, TKeyOut>>> cacheSelector)
            where TKeyOut : notnull
            where TKeyIn : notnull
        {
            /*
             *
             *  Holy schmoly this is not for the feint hearted.
             *
             *  I think the sheer plethora of types is what put me off adding overloads for the specific problem of joining nested caches.
             *
             *  DynamicCombiner is used for the And, Or, Xor and Except operators. These correctly join nested collections as they correctly
             *  handle the adding and removing of inner sources i.e. when a source is removed it's contents should be removed
             *  (unless already within another nested child). It is also highly optimized.
             *
             *  Now we know how fix, we need to add an overload to the Or operator. Do you mind investigating and raising a PR?  If not,
             *  simply copy the combiner into your solution and add this extension.
             *  
             */

            return new DynamicCombiner<TObjectOut, TKeyOut>(source.Transform(cacheSelector).RemoveKey().AsObservableList(), CombineOperator.Or).Run();
        }
    }
}
