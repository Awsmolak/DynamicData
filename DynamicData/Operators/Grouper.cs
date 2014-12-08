﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Configuration;
using DynamicData.Kernel;

namespace DynamicData.Operators
{
    internal sealed class Grouper<TObject, TKey, TGroupKey>
    {
        #region fields

        private readonly IDictionary<TGroupKey, ManagedGroup<TObject, TKey, TGroupKey>> _groupCache = new Dictionary<TGroupKey, ManagedGroup<TObject, TKey, TGroupKey>>();

        private readonly Func<TObject, TGroupKey> _groupSelectorKey;
        private readonly IDictionary<TKey, ChangeWithGroup> _itemCache = new Dictionary<TKey, ChangeWithGroup>();
        private readonly object _locker = new object();

        private struct ChangeWithGroup : IEquatable<ChangeWithGroup>
        {
            private readonly TGroupKey _groupKey;
            private readonly TObject _item;
            private readonly TKey _key;
            private readonly ChangeReason _reason;

            /// <summary>
            ///     Initializes a new instance of the <see cref="T:System.Object" /> class.
            /// </summary>
            public ChangeWithGroup(Change<TObject, TKey> change, Func<TObject, TGroupKey> keySelector)
            {
                _groupKey = keySelector(change.Current);
                _item = change.Current;
                _key = change.Key;
                _reason = change.Reason;
            }

            public TObject Item
            {
                get { return _item; }
            }

            public TKey Key
            {
                get { return _key; }
            }

            public TGroupKey GroupKey
            {
                get { return _groupKey; }
            }

            public ChangeReason Reason
            {
                get { return _reason; }
            }

            #region Equality members

            public bool Equals(ChangeWithGroup other)
            {
                return _key.Equals(other._key);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is ChangeWithGroup && Equals((ChangeWithGroup)obj);
            }

            public override int GetHashCode()
            {
                return _key.GetHashCode();
            }

            public static bool operator ==(ChangeWithGroup left, ChangeWithGroup right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(ChangeWithGroup left, ChangeWithGroup right)
            {
                return !left.Equals(right);
            }

            #endregion

            /// <summary>
            ///     Returns the fully qualified type name of this instance.
            /// </summary>
            /// <returns>
            ///     A <see cref="T:System.String" /> containing a fully qualified type name.
            /// </returns>
            public override string ToString()
            {
                return string.Format("Key: {0}, GroupKey: {1}, Item: {2}", Key, _groupKey,_item);
            }
        }

        #endregion

        #region Construction

        public Grouper(Func<TObject, TGroupKey> groupSelectorKey)
        {
            _groupSelectorKey = groupSelectorKey;
        }

        #endregion

        #region Construction

        public IGroupChangeSet<TObject, TKey, TGroupKey> Update(IChangeSet<TObject, TKey> updates)
        {
            return HandleUpdates(updates);
        }

        public IGroupChangeSet<TObject, TKey, TGroupKey> Regroup()
        {
            //re-evaluate all items in the group
            var items = _itemCache.Select(item => new Change<TObject, TKey>(ChangeReason.Evaluate, item.Key, item.Value.Item));
            return HandleUpdates(new ChangeSet<TObject, TKey>(items), true);
        }

        private GroupChangeSet<TObject, TKey, TGroupKey> HandleUpdates(IEnumerable<Change<TObject, TKey>> changes,bool isRegrouping = false)
        {
            var result = new List<Change<IGroup<TObject, TKey, TGroupKey>, TGroupKey>>();
            //i) evaluate which groups each update should be in 
            var grouped = changes
                .Select(u => new ChangeWithGroup(u, _groupSelectorKey))
                .GroupBy(c=>c.GroupKey)
                .ToList();

            grouped.ForEach(group =>
                            {
                                var groupItem = GetCache(group.Key);
                                var groupCache = groupItem.Item1;
                                if (groupItem.Item2)
                                    result.Add(new Change<IGroup<TObject, TKey, TGroupKey>, TGroupKey>(ChangeReason.Add, group.Key, groupCache));


                                groupCache.Update(updater =>
                                {
                                    group.ForEach(current =>
                                    {

                                        switch (current.Reason)
                                        {
                                            case ChangeReason.Add:
                                            {
                                                updater.AddOrUpdate(current.Item, current.Key);
                                                _itemCache[current.Key] = current;
                                                break;  
                                            }

                                            case ChangeReason.Update:
                                            {
                                                updater.AddOrUpdate(current.Item, current.Key);

                                                //check whether the previous item was in a different group. If so remove from old group
                                                var previous = _itemCache.Lookup(current.Key)
                                                                    .ValueOrThrow(()=>new MissingKeyException("{0} is missing from previous value".FormatWith(current.Key)));

                                                if (previous.GroupKey.Equals(current.GroupKey)) return;

                                                _groupCache.Lookup(previous.GroupKey)
                                                    .IfHasValue(g =>
                                                                {
                                                                    g.Update(u => u.Remove(current.Key));
                                                                    if (g.Count != 0) return;
                                                                    _groupCache.Remove(g.Key);
                                                                    result.Add(new Change<IGroup<TObject, TKey, TGroupKey>, TGroupKey>(ChangeReason.Remove,g.Key,g));
                                                                });
                                                  
                                                _itemCache[current.Key] = current;
                                                break;
                                            }
                                               
                                            case ChangeReason.Remove:
                                            {
                                                var previousInSameGroup = updater.Lookup(current.Key);
                                                if (previousInSameGroup.HasValue)
                                                {
                                                    updater.Remove(current.Key);
                                                }
                                                else
                                                {
                                                    //this has been removed due to an underlying evaluate resulting in a remove
                                                    var previousGroupKey = _itemCache.Lookup(current.Key)
                                                            .ValueOrThrow(()=>new MissingKeyException("{0} is missing from previous value".FormatWith(current.Key)))
                                                            .GroupKey;

                                                   _groupCache.Lookup(previousGroupKey)
                                                    .IfHasValue(g =>
                                                                {
                                                                    g.Update(u => u.Remove(current.Key));
                                                                    if (g.Count!= 0) return;
                                                                    _groupCache.Remove(g.Key);
                                                                    result.Add(new Change<IGroup<TObject, TKey, TGroupKey>, TGroupKey>(ChangeReason.Remove,g.Key,g));
                                                                });
                                                }
                                            }
                                                break;
                                            case ChangeReason.Evaluate:
                                            {
                                                 //check whether the previous item was in a different group. If so remove from old group
                                                var previous = _itemCache.Lookup(current.Key);


                                                previous.IfHasValue(p =>
                                                    {

                                                        if (p.GroupKey.Equals(current.GroupKey))
                                                        {
                                                            //propagate evaluates up the chain
                                                            if (!isRegrouping) updater.Evaluate(current.Key);
                                                            return;
                                                        };

                                                        _groupCache.Lookup(p.GroupKey)
                                                               .IfHasValue(g =>
                                                               {
                                                                   g.Update(u => u.Remove(current.Key));
                                                                   if (g.Count != 0) return;
                                                                   _groupCache.Remove(g.Key);
                                                                   result.Add(new Change<IGroup<TObject, TKey, TGroupKey>, TGroupKey>(ChangeReason.Remove, g.Key, g));
                                                               });

                                                        updater.AddOrUpdate(current.Item, current.Key);

                                                    }).Else(() =>
                                                            {
                                                                //must be created due to addition
                                                                updater.AddOrUpdate(current.Item, current.Key);
                                                            });

                                                _itemCache[current.Key] = current;

                                            }

                                                break;
                                        }
                                    });
                                                      
                                });


                                if (groupCache.Count == 0)
                                {
                                    _groupCache.RemoveIfContained(group.Key);
                                    result.Add(new Change<IGroup<TObject, TKey, TGroupKey>, TGroupKey>(ChangeReason.Remove, group.Key,groupCache));
                                }

                            });
            return new GroupChangeSet<TObject, TKey, TGroupKey>(result);
        }



        #endregion

        private Tuple<ManagedGroup<TObject, TKey, TGroupKey>, bool> GetCache(TGroupKey key)
        {
            Optional<ManagedGroup<TObject, TKey, TGroupKey>> cache = _groupCache.Lookup(key);
            if (cache.HasValue)
            {
                return Tuple.Create(cache.Value, false);
            }
            var newcache = new ManagedGroup<TObject, TKey, TGroupKey>(key);
            _groupCache[key] = newcache;
            return Tuple.Create(newcache, true);
        }
    }
}