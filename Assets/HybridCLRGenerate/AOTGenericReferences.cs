using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"Luban.Runtime.dll",
		"System.Core.dll",
		"UnityEngine.CoreModule.dll",
		"YooAsset.dll",
		"mscorlib.dll",
	};
	// }}

	// {{ constraint implement type
	// }} 

	// {{ AOT generic types
	// System.Action<byte>
	// System.Action<float>
	// System.Action<int>
	// System.Action<long>
	// System.Action<object,object>
	// System.Action<object>
	// System.ArraySegment.ArraySegmentEnumerator<byte>
	// System.ArraySegment<byte>
	// System.Collections.Concurrent.ConcurrentQueue.<Enumerate>d__27<object>
	// System.Collections.Concurrent.ConcurrentQueue.Segment<object>
	// System.Collections.Concurrent.ConcurrentQueue<object>
	// System.Collections.Generic.ArraySortHelper<float>
	// System.Collections.Generic.ArraySortHelper<int>
	// System.Collections.Generic.ArraySortHelper<long>
	// System.Collections.Generic.ArraySortHelper<object>
	// System.Collections.Generic.Comparer<float>
	// System.Collections.Generic.Comparer<int>
	// System.Collections.Generic.Comparer<long>
	// System.Collections.Generic.Comparer<object>
	// System.Collections.Generic.Dictionary.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.Enumerator<object,int>
	// System.Collections.Generic.Dictionary.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,int>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection<long,object>
	// System.Collections.Generic.Dictionary.KeyCollection<object,int>
	// System.Collections.Generic.Dictionary.KeyCollection<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<long,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,int>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection<long,object>
	// System.Collections.Generic.Dictionary.ValueCollection<object,int>
	// System.Collections.Generic.Dictionary.ValueCollection<object,object>
	// System.Collections.Generic.Dictionary<int,object>
	// System.Collections.Generic.Dictionary<long,object>
	// System.Collections.Generic.Dictionary<object,int>
	// System.Collections.Generic.Dictionary<object,object>
	// System.Collections.Generic.EqualityComparer<float>
	// System.Collections.Generic.EqualityComparer<int>
	// System.Collections.Generic.EqualityComparer<long>
	// System.Collections.Generic.EqualityComparer<object>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,int>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.ICollection<float>
	// System.Collections.Generic.ICollection<int>
	// System.Collections.Generic.ICollection<long>
	// System.Collections.Generic.ICollection<object>
	// System.Collections.Generic.IComparer<float>
	// System.Collections.Generic.IComparer<int>
	// System.Collections.Generic.IComparer<long>
	// System.Collections.Generic.IComparer<object>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,int>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerable<float>
	// System.Collections.Generic.IEnumerable<int>
	// System.Collections.Generic.IEnumerable<long>
	// System.Collections.Generic.IEnumerable<object>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<long,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,int>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerator<float>
	// System.Collections.Generic.IEnumerator<int>
	// System.Collections.Generic.IEnumerator<long>
	// System.Collections.Generic.IEnumerator<object>
	// System.Collections.Generic.IEqualityComparer<int>
	// System.Collections.Generic.IEqualityComparer<long>
	// System.Collections.Generic.IEqualityComparer<object>
	// System.Collections.Generic.IList<float>
	// System.Collections.Generic.IList<int>
	// System.Collections.Generic.IList<long>
	// System.Collections.Generic.IList<object>
	// System.Collections.Generic.IReadOnlyCollection<object>
	// System.Collections.Generic.IReadOnlyList<object>
	// System.Collections.Generic.KeyValuePair<int,object>
	// System.Collections.Generic.KeyValuePair<long,object>
	// System.Collections.Generic.KeyValuePair<object,int>
	// System.Collections.Generic.KeyValuePair<object,object>
	// System.Collections.Generic.List.Enumerator<float>
	// System.Collections.Generic.List.Enumerator<int>
	// System.Collections.Generic.List.Enumerator<long>
	// System.Collections.Generic.List.Enumerator<object>
	// System.Collections.Generic.List.SynchronizedList<float>
	// System.Collections.Generic.List.SynchronizedList<int>
	// System.Collections.Generic.List.SynchronizedList<long>
	// System.Collections.Generic.List.SynchronizedList<object>
	// System.Collections.Generic.List<float>
	// System.Collections.Generic.List<int>
	// System.Collections.Generic.List<long>
	// System.Collections.Generic.List<object>
	// System.Collections.Generic.ObjectComparer<float>
	// System.Collections.Generic.ObjectComparer<int>
	// System.Collections.Generic.ObjectComparer<long>
	// System.Collections.Generic.ObjectComparer<object>
	// System.Collections.Generic.ObjectEqualityComparer<float>
	// System.Collections.Generic.ObjectEqualityComparer<int>
	// System.Collections.Generic.ObjectEqualityComparer<long>
	// System.Collections.Generic.ObjectEqualityComparer<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<float>
	// System.Collections.ObjectModel.ReadOnlyCollection<int>
	// System.Collections.ObjectModel.ReadOnlyCollection<long>
	// System.Collections.ObjectModel.ReadOnlyCollection<object>
	// System.Comparison<float>
	// System.Comparison<int>
	// System.Comparison<long>
	// System.Comparison<object>
	// System.Converter<long,object>
	// System.Func<System.Threading.Tasks.VoidTaskResult>
	// System.Func<byte>
	// System.Func<object,System.Threading.Tasks.VoidTaskResult>
	// System.Func<object,byte>
	// System.Func<object,object,object>
	// System.Func<object,object>
	// System.Func<object>
	// System.Nullable<int>
	// System.Predicate<float>
	// System.Predicate<int>
	// System.Predicate<long>
	// System.Predicate<object>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<byte>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<object>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<byte>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<object>
	// System.Runtime.CompilerServices.TaskAwaiter<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.TaskAwaiter<byte>
	// System.Runtime.CompilerServices.TaskAwaiter<object>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<byte>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<object>
	// System.Threading.Tasks.Task.<>c<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.Task.<>c<byte>
	// System.Threading.Tasks.Task.<>c<object>
	// System.Threading.Tasks.Task<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.Task<byte>
	// System.Threading.Tasks.Task<object>
	// System.Threading.Tasks.TaskCompletionSource<object>
	// System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_0<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_0<byte>
	// System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_0<object>
	// System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_1<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_1<byte>
	// System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_1<object>
	// System.Threading.Tasks.TaskFactory<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.TaskFactory<byte>
	// System.Threading.Tasks.TaskFactory<object>
	// System.ValueTuple<object,object,object>
	// }}

	public void RefMethods()
	{
		// string Luban.StringUtil.CollectionToString<float>(System.Collections.Generic.IEnumerable<float>)
		// string Luban.StringUtil.CollectionToString<int>(System.Collections.Generic.IEnumerable<int>)
		// System.Collections.Generic.List<object> System.Collections.Generic.List<long>.ConvertAll<object>(System.Converter<long,object>)
		// object System.Linq.Enumerable.FirstOrDefault<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.AuctionPanel.<AutoRefresh>d__14>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.AuctionPanel.<AutoRefresh>d__14&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.AuctionPanel.<Bind>d__11>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.AuctionPanel.<Bind>d__11&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.ChestBoard.<Bind>d__22>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.ChestBoard.<Bind>d__22&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.ChestBoard.<RefreshData>d__26>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.ChestBoard.<RefreshData>d__26&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<MaybeShowTutorial>d__10>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<MaybeShowTutorial>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<RefreshFeatureGates>d__12>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<RefreshFeatureGates>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.AuctionPanel.<RefreshData>d__12>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.AuctionPanel.<RefreshData>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.DuelWsClient.<ReceiveLoop>d__7>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.DuelWsClient.<ReceiveLoop>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.RushWsClient.<ReceiveLoop>d__7>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.RushWsClient.<ReceiveLoop>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<RefreshRank>d__21>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<RefreshRank>d__21&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.AuctionPanel.<AutoRefresh>d__14>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.AuctionPanel.<AutoRefresh>d__14&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.AuctionPanel.<Bind>d__11>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.AuctionPanel.<Bind>d__11&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.ChestBoard.<Bind>d__22>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.ChestBoard.<Bind>d__22&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.ChestBoard.<RefreshData>d__26>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.ChestBoard.<RefreshData>d__26&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<MaybeShowTutorial>d__10>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<MaybeShowTutorial>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<RefreshFeatureGates>d__12>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<RefreshFeatureGates>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.AuctionPanel.<RefreshData>d__12>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.AuctionPanel.<RefreshData>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.DuelWsClient.<ReceiveLoop>d__7>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.DuelWsClient.<ReceiveLoop>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.RushWsClient.<ReceiveLoop>d__7>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.RushWsClient.<ReceiveLoop>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<RefreshRank>d__21>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<RefreshRank>d__21&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.ApiClient.<LoginWithIdentityAsync>d__23>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.ApiClient.<LoginWithIdentityAsync>d__23&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.LoginBridge.<LoginAsync>d__4>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.LoginBridge.<LoginAsync>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ApiClient.<LoginAsync>d__22>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ApiClient.<LoginAsync>d__22&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ApiClient.<LoginWithIdentityAsync>d__23>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ApiClient.<LoginWithIdentityAsync>d__23&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.LoginBridge.<LoginAsync>d__4>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.LoginBridge.<LoginAsync>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.OnlineState.<Refresh>d__22>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.OnlineState.<Refresh>d__22&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ApiClient.<GetAsync>d__51>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ApiClient.<GetAsync>d__51&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ApiClient.<PostAsync>d__52>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ApiClient.<PostAsync>d__52&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ApiClient.<SendAsync>d__53>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ApiClient.<SendAsync>d__53&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.LoginBridge.<GetIdentityAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.LoginBridge.<GetIdentityAsync>d__5&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.PayBridge.<PurchaseAsync>d__7>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.PayBridge.<PurchaseAsync>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.PayBridge.<QuerySkuAsync>d__6>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.PayBridge.<QuerySkuAsync>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<YooAsset.OperationAwaiter,Game.HotUpdate.ChestGame.UIManager.<ShowAsync>d__10>(YooAsset.OperationAwaiter&,Game.HotUpdate.ChestGame.UIManager.<ShowAsync>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.AuctionPanel.<AutoRefresh>d__14>(Game.HotUpdate.ChestGame.AuctionPanel.<AutoRefresh>d__14&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.AuctionPanel.<Bind>d__11>(Game.HotUpdate.ChestGame.AuctionPanel.<Bind>d__11&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.AuctionPanel.<RefreshData>d__12>(Game.HotUpdate.ChestGame.AuctionPanel.<RefreshData>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33>(Game.HotUpdate.ChestGame.ChestBoard.<AutoDecline>d__33&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<Bind>d__22>(Game.HotUpdate.ChestGame.ChestBoard.<Bind>d__22&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<RefreshData>d__26>(Game.HotUpdate.ChestGame.ChestBoard.<RefreshData>d__26&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.DuelWsClient.<ReceiveLoop>d__7>(Game.HotUpdate.ChestGame.DuelWsClient.<ReceiveLoop>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.RushWsClient.<ReceiveLoop>d__7>(Game.HotUpdate.ChestGame.RushWsClient.<ReceiveLoop>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<MaybeShowTutorial>d__10>(Game.HotUpdate.ChestGame.Screens.<MaybeShowTutorial>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<RefreshFeatureGates>d__12>(Game.HotUpdate.ChestGame.Screens.<RefreshFeatureGates>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<RefreshRank>d__21>(Game.HotUpdate.ChestGame.Screens.<RefreshRank>d__21&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.Start<Game.HotUpdate.ChestGame.ApiClient.<LoginAsync>d__22>(Game.HotUpdate.ChestGame.ApiClient.<LoginAsync>d__22&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.Start<Game.HotUpdate.ChestGame.ApiClient.<LoginWithIdentityAsync>d__23>(Game.HotUpdate.ChestGame.ApiClient.<LoginWithIdentityAsync>d__23&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.Start<Game.HotUpdate.ChestGame.LoginBridge.<LoginAsync>d__4>(Game.HotUpdate.ChestGame.LoginBridge.<LoginAsync>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.Start<Game.HotUpdate.ChestGame.OnlineState.<Refresh>d__22>(Game.HotUpdate.ChestGame.OnlineState.<Refresh>d__22&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.ChestGame.ApiClient.<GetAsync>d__51>(Game.HotUpdate.ChestGame.ApiClient.<GetAsync>d__51&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.ChestGame.ApiClient.<PostAsync>d__52>(Game.HotUpdate.ChestGame.ApiClient.<PostAsync>d__52&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.ChestGame.ApiClient.<SendAsync>d__53>(Game.HotUpdate.ChestGame.ApiClient.<SendAsync>d__53&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.ChestGame.LoginBridge.<GetIdentityAsync>d__5>(Game.HotUpdate.ChestGame.LoginBridge.<GetIdentityAsync>d__5&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.ChestGame.PayBridge.<PurchaseAsync>d__7>(Game.HotUpdate.ChestGame.PayBridge.<PurchaseAsync>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.ChestGame.PayBridge.<QuerySkuAsync>d__6>(Game.HotUpdate.ChestGame.PayBridge.<QuerySkuAsync>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.ChestGame.UIManager.<ShowAsync>d__10>(Game.HotUpdate.ChestGame.UIManager.<ShowAsync>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.AuctionPanel.<OnBid>d__16>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.AuctionPanel.<OnBid>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.AuctionPanel.<OnBuyout>d__17>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.AuctionPanel.<OnBuyout>d__17&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.AuctionPanel.<Show>d__10>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.AuctionPanel.<Show>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_1>d>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.ChestBoard.<OnEliminate>d__29>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.ChestBoard.<OnEliminate>d__29&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.ChestBoard.<OnSkill>d__34>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.ChestBoard.<OnSkill>d__34&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.ChestBoard.<Show>d__21>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.ChestBoard.<Show>d__21&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.DuelWsClient.<Connect>d__6>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.DuelWsClient.<Connect>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.RushWsClient.<Connect>d__6>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.RushWsClient.<Connect>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ChestGame.Screens.<ShowRank>d__20>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ChestGame.Screens.<ShowRank>d__20&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.AuctionPanel.<>c__DisplayClass18_0.<<OpenListDialog>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.AuctionPanel.<>c__DisplayClass18_0.<<OpenListDialog>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass16_1.<<ShowAdCenter>b__2>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass16_1.<<ShowAdCenter>b__2>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass19_0.<<ShowLogin>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass19_0.<<ShowLogin>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass8_0.<<ShowDaily>b__1>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass8_0.<<ShowDaily>b__1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<ShowAdCenter>d__16>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<ShowAdCenter>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<ShowInvite>d__13>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<ShowInvite>d__13&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<ShowQuests>d__15>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<ShowQuests>d__15&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.Screens.<ShowSign>d__14>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.Screens.<ShowSign>d__14&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ChestGame.UIRoot.<RefreshTop>d__15>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ChestGame.UIRoot.<RefreshTop>d__15&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.AuctionPanel.<>c__DisplayClass18_0.<<OpenListDialog>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.AuctionPanel.<>c__DisplayClass18_0.<<OpenListDialog>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.AuctionPanel.<OnBid>d__16>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.AuctionPanel.<OnBid>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.AuctionPanel.<OnBuyout>d__17>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.AuctionPanel.<OnBuyout>d__17&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.AuctionPanel.<Show>d__10>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.AuctionPanel.<Show>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_1>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ChestBoard.<OnEliminate>d__29>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ChestBoard.<OnEliminate>d__29&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ChestBoard.<OnSell>d__36>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ChestBoard.<OnSell>d__36&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ChestBoard.<OnSkill>d__34>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ChestBoard.<OnSkill>d__34&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ChestBoard.<Show>d__21>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ChestBoard.<Show>d__21&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.DuelPanel.<Show>d__24>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.DuelPanel.<Show>d__24&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.RushPanel.<>c__DisplayClass11_0.<<BuildRoomCards>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.RushPanel.<>c__DisplayClass11_0.<<BuildRoomCards>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.RushPanel.<Show>d__9>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.RushPanel.<Show>d__9&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass10_0.<<MaybeShowTutorial>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass10_0.<<MaybeShowTutorial>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass13_1.<<ShowInvite>b__2>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass13_1.<<ShowInvite>b__2>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass14_0.<<ShowSign>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass14_0.<<ShowSign>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass14_0.<<ShowSign>b__3>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass14_0.<<ShowSign>b__3>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass15_1.<<ShowQuests>b__1>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass15_1.<<ShowQuests>b__1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass15_1.<<ShowQuests>b__3>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass15_1.<<ShowQuests>b__3>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass16_1.<<ShowAdCenter>b__2>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass16_1.<<ShowAdCenter>b__2>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass23_0.<<ShowShop>b__1>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass23_0.<<ShowShop>b__1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass24_0.<<BuildItemCards>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass24_0.<<BuildItemCards>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass25_0.<<PromptRename>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass25_0.<<PromptRename>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass8_0.<<ShowDaily>b__1>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass8_0.<<ShowDaily>b__1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<ShowBag>d__0>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<ShowBag>d__0&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<ShowDaily>d__8>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<ShowDaily>d__8&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<ShowLogin>d__19>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<ShowLogin>d__19&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<ShowMain>d__9>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<ShowMain>d__9&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<ShowRank>d__20>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<ShowRank>d__20&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.Screens.<ShowShop>d__23>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.Screens.<ShowShop>d__23&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ChestGame.ShareBridge.<Share>d__4>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ChestGame.ShareBridge.<Share>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.AuctionPanel.<>c__DisplayClass18_0.<<OpenListDialog>b__0>d>(Game.HotUpdate.ChestGame.AuctionPanel.<>c__DisplayClass18_0.<<OpenListDialog>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.AuctionPanel.<OnBid>d__16>(Game.HotUpdate.ChestGame.AuctionPanel.<OnBid>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.AuctionPanel.<OnBuyout>d__17>(Game.HotUpdate.ChestGame.AuctionPanel.<OnBuyout>d__17&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.AuctionPanel.<Show>d__10>(Game.HotUpdate.ChestGame.AuctionPanel.<Show>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_0>d>(Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_1>d>(Game.HotUpdate.ChestGame.ChestBoard.<<RefreshOfferPanel>b__25_1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<OnEliminate>d__29>(Game.HotUpdate.ChestGame.ChestBoard.<OnEliminate>d__29&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<OnSell>d__36>(Game.HotUpdate.ChestGame.ChestBoard.<OnSell>d__36&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<OnSkill>d__34>(Game.HotUpdate.ChestGame.ChestBoard.<OnSkill>d__34&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.ChestBoard.<Show>d__21>(Game.HotUpdate.ChestGame.ChestBoard.<Show>d__21&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.DuelPanel.<Show>d__24>(Game.HotUpdate.ChestGame.DuelPanel.<Show>d__24&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.DuelWsClient.<Connect>d__6>(Game.HotUpdate.ChestGame.DuelWsClient.<Connect>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.RushPanel.<>c__DisplayClass11_0.<<BuildRoomCards>b__0>d>(Game.HotUpdate.ChestGame.RushPanel.<>c__DisplayClass11_0.<<BuildRoomCards>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.RushPanel.<Show>d__9>(Game.HotUpdate.ChestGame.RushPanel.<Show>d__9&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.RushWsClient.<Connect>d__6>(Game.HotUpdate.ChestGame.RushWsClient.<Connect>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass10_0.<<MaybeShowTutorial>b__0>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass10_0.<<MaybeShowTutorial>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass13_1.<<ShowInvite>b__2>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass13_1.<<ShowInvite>b__2>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass14_0.<<ShowSign>b__0>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass14_0.<<ShowSign>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass14_0.<<ShowSign>b__3>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass14_0.<<ShowSign>b__3>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass15_1.<<ShowQuests>b__1>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass15_1.<<ShowQuests>b__1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass15_1.<<ShowQuests>b__3>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass15_1.<<ShowQuests>b__3>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass16_1.<<ShowAdCenter>b__2>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass16_1.<<ShowAdCenter>b__2>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass19_0.<<ShowLogin>b__0>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass19_0.<<ShowLogin>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass23_0.<<ShowShop>b__1>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass23_0.<<ShowShop>b__1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass24_0.<<BuildItemCards>b__0>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass24_0.<<BuildItemCards>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass25_0.<<PromptRename>b__0>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass25_0.<<PromptRename>b__0>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass8_0.<<ShowDaily>b__1>d>(Game.HotUpdate.ChestGame.Screens.<>c__DisplayClass8_0.<<ShowDaily>b__1>d&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowAdCenter>d__16>(Game.HotUpdate.ChestGame.Screens.<ShowAdCenter>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowBag>d__0>(Game.HotUpdate.ChestGame.Screens.<ShowBag>d__0&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowDaily>d__8>(Game.HotUpdate.ChestGame.Screens.<ShowDaily>d__8&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowInvite>d__13>(Game.HotUpdate.ChestGame.Screens.<ShowInvite>d__13&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowLogin>d__19>(Game.HotUpdate.ChestGame.Screens.<ShowLogin>d__19&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowMain>d__9>(Game.HotUpdate.ChestGame.Screens.<ShowMain>d__9&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowQuests>d__15>(Game.HotUpdate.ChestGame.Screens.<ShowQuests>d__15&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowRank>d__20>(Game.HotUpdate.ChestGame.Screens.<ShowRank>d__20&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowShop>d__23>(Game.HotUpdate.ChestGame.Screens.<ShowShop>d__23&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.Screens.<ShowSign>d__14>(Game.HotUpdate.ChestGame.Screens.<ShowSign>d__14&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.ShareBridge.<Share>d__4>(Game.HotUpdate.ChestGame.ShareBridge.<Share>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncVoidMethodBuilder.Start<Game.HotUpdate.ChestGame.UIRoot.<RefreshTop>d__15>(Game.HotUpdate.ChestGame.UIRoot.<RefreshTop>d__15&)
		// object UnityEngine.Component.GetComponent<object>()
		// object UnityEngine.Component.GetComponentInChildren<object>()
		// object UnityEngine.Component.GetComponentInParent<object>()
		// object[] UnityEngine.Component.GetComponentsInChildren<object>(bool)
		// object UnityEngine.GameObject.AddComponent<object>()
		// object UnityEngine.GameObject.GetComponent<object>()
		// object UnityEngine.GameObject.GetComponentInChildren<object>()
		// object UnityEngine.GameObject.GetComponentInChildren<object>(bool)
		// object[] UnityEngine.GameObject.GetComponentsInChildren<object>(bool)
		// object UnityEngine.Object.FindObjectOfType<object>()
		// object UnityEngine.Resources.GetBuiltinResource<object>(string)
		// YooAsset.AssetHandle YooAsset.ResourcePackage.LoadAssetAsync<object>(string,uint)
		// YooAsset.AssetHandle YooAsset.ResourcePackage.LoadAssetSync<object>(string)
		// string string.Join<float>(string,System.Collections.Generic.IEnumerable<float>)
		// string string.Join<int>(string,System.Collections.Generic.IEnumerable<int>)
	}
}