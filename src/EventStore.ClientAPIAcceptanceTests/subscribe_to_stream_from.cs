using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EventStore.ClientAPI.Tests {
	public class subscribe_to_stream_from : EventStoreClientAPITest {
		private readonly EventStoreClientAPIFixture _fixture;

		public subscribe_to_stream_from(EventStoreClientAPIFixture fixture) {
			_fixture = fixture;
		}

		[Theory, MemberData(nameof(UseSslTestCases))]
		public async Task from_non_existing_stream(bool useSsl) {
			var streamName = $"{GetStreamName()}_{useSsl}";
			var eventAppearedSource = new TaskCompletionSource<ResolvedEvent>();
			var connection = _fixture.Connections[useSsl];

			connection.SubscribeToStreamFrom(streamName, default, CatchUpSubscriptionSettings.Default,
				EventAppeared, subscriptionDropped: SubscriptionDropped);

			var testEvents = _fixture.CreateTestEvents().ToArray();
			await connection.AppendToStreamAsync(streamName, ExpectedVersion.NoStream, testEvents).WithTimeout();

			var resolvedEvent = await eventAppearedSource.Task.WithTimeout();

			Assert.Equal(testEvents[0].EventId, resolvedEvent.OriginalEvent.EventId);

			Task EventAppeared(EventStoreCatchUpSubscription s, ResolvedEvent e) {
				eventAppearedSource.TrySetResult(e);
				return Task.CompletedTask;
			}

			void SubscriptionDropped(EventStoreCatchUpSubscription s, SubscriptionDropReason reason, Exception ex) =>
				eventAppearedSource.TrySetException(ex ?? new ObjectDisposedException(nameof(s)));
		}


		[Theory, MemberData(nameof(UseSslTestCases))]
		public async Task concurrently(bool useSsl) {
			var streamName = $"{GetStreamName()}_{useSsl}";
			var eventAppearedSource1 = new TaskCompletionSource<ResolvedEvent>();
			var eventAppearedSource2 = new TaskCompletionSource<ResolvedEvent>();
			var connection = _fixture.Connections[useSsl];

			connection.SubscribeToStreamFrom(streamName, default, CatchUpSubscriptionSettings.Default,
				EventAppeared1, subscriptionDropped: SubscriptionDropped1);

			connection.SubscribeToStreamFrom(streamName, default, CatchUpSubscriptionSettings.Default,
				EventAppeared2, subscriptionDropped: SubscriptionDropped2);

			var testEvents = _fixture.CreateTestEvents().ToArray();
			await connection.AppendToStreamAsync(streamName, ExpectedVersion.NoStream, testEvents).WithTimeout();

			var resolvedEvents =
				await Task.WhenAll(eventAppearedSource1.Task, eventAppearedSource2.Task).WithTimeout();

			Assert.Equal(testEvents[0].EventId, resolvedEvents[0].OriginalEvent.EventId);
			Assert.Equal(testEvents[0].EventId, resolvedEvents[1].OriginalEvent.EventId);


			Task EventAppeared1(EventStoreCatchUpSubscription s, ResolvedEvent e) {
				eventAppearedSource1.TrySetResult(e);
				return Task.CompletedTask;
			}

			Task EventAppeared2(EventStoreCatchUpSubscription s, ResolvedEvent e) {
				eventAppearedSource2.TrySetResult(e);
				return Task.CompletedTask;
			}

			void SubscriptionDropped1(EventStoreCatchUpSubscription s, SubscriptionDropReason reason, Exception ex) =>
				eventAppearedSource1.TrySetException(ex ?? new ObjectDisposedException(nameof(s)));

			void SubscriptionDropped2(EventStoreCatchUpSubscription s, SubscriptionDropReason reason, Exception ex) =>
				eventAppearedSource2.TrySetException(ex ?? new ObjectDisposedException(nameof(s)));
		}

		[Theory, MemberData(nameof(UseSslTestCases))]
		public async Task drops_on_subscriber_error(bool useSsl) {
			var streamName = $"{GetStreamName()}_{useSsl}";
			var droppedSource = new TaskCompletionSource<(SubscriptionDropReason, Exception)>();
			var expectedException = new Exception("subscriber error");
			var connection = _fixture.Connections[useSsl];

			connection.SubscribeToStreamFrom(streamName, default, CatchUpSubscriptionSettings.Default,
				EventAppeared, subscriptionDropped: SubscriptionDropped);

			var testEvents = _fixture.CreateTestEvents().ToArray();
			await connection.AppendToStreamAsync(streamName, ExpectedVersion.NoStream, testEvents).WithTimeout();

			var (dropped, exception) = await droppedSource.Task.WithTimeout();

			Assert.Equal(SubscriptionDropReason.EventHandlerException, dropped);
			Assert.IsType(expectedException.GetType(), exception);
			Assert.Equal(expectedException.Message, exception.Message);

			Task EventAppeared(EventStoreCatchUpSubscription s, ResolvedEvent e)
				=> Task.FromException(expectedException);

			void SubscriptionDropped(EventStoreCatchUpSubscription s, SubscriptionDropReason reason, Exception ex) =>
				droppedSource.TrySetResult((reason, ex));
		}

		[Theory, MemberData(nameof(UseSslTestCases))]
		public async Task drops_on_unsubscribed(bool useSsl) {
			var streamName = $"{GetStreamName()}_{useSsl}";
			var droppedSource = new TaskCompletionSource<(SubscriptionDropReason, Exception)>();
			var connection = _fixture.Connections[useSsl];

			var subscription = connection.SubscribeToStreamFrom(streamName, 0L, CatchUpSubscriptionSettings.Default,
				EventAppeared, subscriptionDropped: SubscriptionDropped);

			subscription.Stop(TimeSpan.FromSeconds(3));

			var (dropped, exception) = await droppedSource.Task.WithTimeout();

			Assert.Equal(SubscriptionDropReason.UserInitiated, dropped);
			Assert.Null(exception);

			Task EventAppeared(EventStoreCatchUpSubscription s, ResolvedEvent e)
				=> Task.CompletedTask;

			void SubscriptionDropped(EventStoreCatchUpSubscription s, SubscriptionDropReason reason, Exception ex) =>
				droppedSource.TrySetResult((reason, ex));
		}
	}
}
