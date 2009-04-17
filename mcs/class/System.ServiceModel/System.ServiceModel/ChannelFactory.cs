//
// ChannelFactory.cs
//
// Author:
//	Atsushi Enomoto <atsushi@ximian.com>
//
// Copyright (C) 2005 Novell, Inc.  http://www.novell.com
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections.ObjectModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Security;
using System.Configuration;
using System.ServiceModel.Configuration;
using System.Xml;

namespace System.ServiceModel
{
	public abstract class ChannelFactory : CommunicationObject,
		IChannelFactory, ICommunicationObject, IDisposable
	{
		// instance members

		ServiceEndpoint service_endpoint;
		IChannelFactory factory;

		protected ChannelFactory ()
		{
		}

		internal IChannelFactory OpenedChannelFactory {
			get {
				if (factory == null) {
					factory = CreateFactory ();
					factory.Open ();
				}

				return factory;
			}
			private set {
				factory = value;
			}
		}

		public ServiceEndpoint Endpoint {
			get { return service_endpoint; }
		}

#if !NET_2_1
		public ClientCredentials Credentials {
			get { return Endpoint.Behaviors.Find<ClientCredentials> (); }
		}
#endif

		protected internal override TimeSpan DefaultCloseTimeout {
			get { return Endpoint.Binding.CloseTimeout; }
		}

		protected internal override TimeSpan DefaultOpenTimeout {
			get { return Endpoint.Binding.OpenTimeout; }
		}

		protected virtual void ApplyConfiguration (string endpointConfig)
		{
			if (endpointConfig == null)
				return;

#if NET_2_1
			try {
				// It should automatically use XmlXapResolver
				var cfg = new SilverlightClientConfigLoader ().Load (XmlReader.Create ("ServiceReferences.ClientConfig"));

				SilverlightClientConfigLoader.ServiceEndpointConfiguration se = null;
				if (endpointConfig == "*")
					se = cfg.GetServiceEndpointConfiguration (Endpoint.Contract.Name);
				if (se == null)
					se = cfg.GetServiceEndpointConfiguration (endpointConfig);

				if (se.Binding != null && Endpoint.Binding == null)
					Endpoint.Binding = se.Binding;
				else // ignore it
					Console.WriteLine ("WARNING: Configured binding not found in configuration {0}", endpointConfig);
				if (se.Address != null && Endpoint.Address == null)
					Endpoint.Address = se.Address;
				else // ignore it
					Console.WriteLine ("WARNING: Configured endpoint address not found in configuration {0}", endpointConfig);
			} catch (Exception) {
				// ignore it.
				Console.WriteLine ("WARNING: failed to load endpoint configuration for {0}", endpointConfig);
			}
#else

			string contractName = Endpoint.Contract.ConfigurationName;
			ClientSection client = (ClientSection) ConfigurationManager.GetSection ("system.serviceModel/client");
			ChannelEndpointElement res = null;
			foreach (ChannelEndpointElement el in client.Endpoints) {
				if (el.Contract == contractName && (endpointConfig == el.Name || endpointConfig == "*")) {
					if (res != null)
						throw new InvalidOperationException (String.Format ("More then one endpoint matching contract {0} was found.", contractName));
					res = el;
				}
			}

			if (res == null)
				throw new InvalidOperationException (String.Format ("Client endpoint configuration '{0}' was not found in {1} endpoints.", endpointConfig, client.Endpoints.Count));

			if (Endpoint.Binding == null)
				Endpoint.Binding = ConfigUtil.CreateBinding (res.Binding, res.BindingConfiguration);
			if (Endpoint.Address == null)
				Endpoint.Address = new EndpointAddress (res.Address);

			if (res.BehaviorConfiguration != "")
				ApplyBehavior (res.BehaviorConfiguration);
#endif
		}

#if !NET_2_1
		private void ApplyBehavior (string behaviorConfig)
		{
			BehaviorsSection behaviorsSection = (BehaviorsSection) ConfigurationManager.GetSection ("system.serviceModel/behaviors");
			EndpointBehaviorElement behaviorElement = behaviorsSection.EndpointBehaviors [behaviorConfig];
			int i = 0;
			foreach (BehaviorExtensionElement el in behaviorElement) {
				IEndpointBehavior behavior = (IEndpointBehavior) el.CreateBehavior ();
				Endpoint.Behaviors.Remove (behavior.GetType ());
				Endpoint.Behaviors.Add (behavior);
			}
		}
#endif

		protected virtual IChannelFactory CreateFactory ()
		{
			bool session = false;
			bool isOneWay = true; // check OperationDescription.IsOneWay
			bool isDuplex = Endpoint.Contract.CallbackContractType != null;

			foreach (var od in Endpoint.Contract.Operations)
				if (!od.IsOneWay) {
					isOneWay = false;
					break;
				}

			BindingParameterCollection pl = CreateBindingParameters ();

			if (isDuplex) {
				switch (Endpoint.Contract.SessionMode) {
				case SessionMode.Required:
					if (Endpoint.Binding.CanBuildChannelFactory<IDuplexSessionChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IDuplexSessionChannel> (pl);
					throw new InvalidOperationException ("The contract requires session channel, but the binding does not support it.");
				case SessionMode.Allowed:
					if (Endpoint.Binding.CanBuildChannelFactory<IDuplexChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IDuplexChannel> (pl);
					goto case SessionMode.Required;
				default:
					if (Endpoint.Binding.CanBuildChannelFactory<IDuplexChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IDuplexChannel> (pl);
					throw new InvalidOperationException ("The contract requires non-session channel, but the binding does not support it.");
				}
			} else if (isOneWay) {
				switch (Endpoint.Contract.SessionMode) {
				case SessionMode.Required:
					if (Endpoint.Binding.CanBuildChannelFactory<IOutputSessionChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IOutputSessionChannel> (pl);
					throw new InvalidOperationException ("The contract requires session channel, but the binding does not support it.");
				case SessionMode.Allowed:
					if (Endpoint.Binding.CanBuildChannelFactory<IOutputChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IOutputChannel> (pl);
					goto case SessionMode.Required;
				default:
					if (Endpoint.Binding.CanBuildChannelFactory<IOutputChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IOutputChannel> (pl);
					throw new InvalidOperationException ("The contract requires non-session channel, but the binding does not support it.");
				}
			} else {
				switch (Endpoint.Contract.SessionMode) {
				case SessionMode.Required:
					if (Endpoint.Binding.CanBuildChannelFactory<IRequestSessionChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IRequestSessionChannel> (pl);
					throw new InvalidOperationException ("The contract requires session channel, but the binding does not support it.");
				case SessionMode.Allowed:
					if (Endpoint.Binding.CanBuildChannelFactory<IRequestChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IRequestChannel> (pl);
					goto case SessionMode.Required;
				default:
					if (Endpoint.Binding.CanBuildChannelFactory<IRequestChannel> (pl))
						return Endpoint.Binding.BuildChannelFactory<IRequestChannel> (pl);
					throw new InvalidOperationException ("The contract requires non-session channel, but the binding does not support it.");
				}
			}
		}

		BindingParameterCollection CreateBindingParameters ()
		{
			BindingParameterCollection pl =
				new BindingParameterCollection ();

			ContractDescription cd = Endpoint.Contract;
#if !NET_2_1
			pl.Add (ChannelProtectionRequirements.CreateFromContract (cd));

			foreach (IEndpointBehavior behavior in Endpoint.Behaviors)
				behavior.AddBindingParameters (Endpoint, pl);
#endif

			return pl;
		}

		protected abstract ServiceEndpoint CreateDescription ();

		void IDisposable.Dispose ()
		{
			Close ();
		}

		[MonoTODO]
		public T GetProperty<T> () where T : class
		{
			throw new NotImplementedException ();
		}

		protected void EnsureOpened ()
		{
			if (State != CommunicationState.Opened)
				Open ();
		}

		protected void InitializeEndpoint (
			string endpointConfigurationName,
			EndpointAddress remoteAddress)
		{
			InitializeEndpoint (CreateDescription ());
			service_endpoint.Address = remoteAddress;
			ApplyConfiguration (endpointConfigurationName);
		}

		protected void InitializeEndpoint (Binding binding,
			EndpointAddress remoteAddress)
		{
			InitializeEndpoint (CreateDescription ());
			service_endpoint.Binding = binding;
			service_endpoint.Address = remoteAddress;
		}

		protected void InitializeEndpoint (ServiceEndpoint endpoint)
		{
			service_endpoint = endpoint;
		}

		[MonoTODO]
		protected override void OnAbort ()
		{
			throw new NotImplementedException ();
		}

		[MonoTODO]
		protected override IAsyncResult OnBeginClose (
			TimeSpan timeout, AsyncCallback callback, object state)
		{
			throw new NotImplementedException ();
		}

		[MonoTODO]
		protected override IAsyncResult OnBeginOpen (
			TimeSpan timeout, AsyncCallback callback, object state)
		{
			throw new NotImplementedException ();
		}

		[MonoTODO]
		protected override void OnEndClose (IAsyncResult result)
		{
			throw new NotImplementedException ();
		}

		[MonoTODO]
		protected override void OnEndOpen (IAsyncResult result)
		{
			throw new NotImplementedException ();
		}

		[MonoTODO]
		protected override void OnClose (TimeSpan timeout)
		{
		}

		[MonoTODO]
		protected override void OnOpen (TimeSpan timeout)
		{
		}

		protected override void OnOpening ()
		{
			OpenedChannelFactory = CreateFactory ();
			OpenedChannelFactory.Open ();
		}

		protected override void OnOpened ()
		{
		}
	}

#if obsolete
	[ServiceContract]
	interface UninitializedContract
	{
		[OperationContract]
		void ItShouldReallyGone ();
	}
#endif
}
