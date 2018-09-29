module EventStore
(*
    This implementation was copied from
    https://blog.2mas.xyz/fsharp-event-sourcing-and-cqrs-tutorial-and-agents/
    and then modified.
*)

type Agent<'TMessage> = MailboxProcessor<'TMessage>  

type StreamId = StreamId of System.Guid
and StreamVersion = StreamVersion of int

type SaveResult =  
    | Ok
    | VersionConflict of VersionConflict
and VersionConflict = {Expected: StreamVersion; Actual: StreamVersion}
type Messages<'t> =  
    | GetEvents of EventRequest<'t>
    | SaveEvents of NewEvents<'t>
    | AddSubscriber of NewSubscriber<'t>
    | RemoveSubscriber of SubscriptionToken
and NewEvents<'t> = 
    { StreamId: StreamId
    ; StreamVersion: StreamVersion
    ; Events: 't list
    ; ReplyChannel: AsyncReplyChannel<SaveResult>
    }
and NewSubscriber<'t> =
    { SubscriptionToken: SubscriptionToken
    ; EventHandler: StreamId * 't list -> unit
    }
and SubscriptionToken = SubscriptionToken of string
and EventRequest<'t> =
    { StreamId: StreamId
    ; ReplyChannel: AsyncReplyChannel<'t list option>
    }

type internal EventStoreState<'event,'THandler> =  
    { EventHandler: 'THandler
    ; GetEvents: 'THandler -> StreamId -> ('event list option * 'THandler) 
    ; SaveEvents: 'THandler -> StreamId -> StreamVersion -> 'event list -> (SaveResult * 'THandler)
    ; Subscribers: Map<SubscriptionToken, (StreamId * 'event list -> unit)>
    }

let createEventStoreAgent<'T, 'TEventHandler> (eventHandler:'TEventHandler) getEvents saveEvents =
    MailboxProcessor.Start(fun (inbox:Agent<Messages<'T>>) ->
        let initState = 
            { EventHandler = eventHandler
            ; Subscribers = Map.empty
            ; GetEvents = getEvents
            ; SaveEvents = saveEvents
            }
        let rec loop state = 
            async {
                let! msg = inbox.Receive()
                match msg with
                | GetEvents eq ->
                    let (events, newHandler) = state.GetEvents state.EventHandler eq.StreamId
                    eq.ReplyChannel.Reply(events)
                    return! loop {state with EventHandler = newHandler}
                | SaveEvents batch ->
                    let (result, newHandler) = state.SaveEvents state.EventHandler batch.StreamId batch.StreamVersion batch.Events
                    if result = Ok then state.Subscribers |> Map.iter (fun _ sub -> sub(batch.StreamId, batch.Events)) else ()
                    batch.ReplyChannel.Reply(result)
                    return! loop {state with EventHandler = newHandler}
                | AddSubscriber s ->
                    let newState = {state with Subscribers = (state.Subscribers |> Map.add s.SubscriptionToken s.EventHandler)}
                    return! loop newState
                | RemoveSubscriber subId ->
                    let newState = {state with Subscribers = (state.Subscribers |> Map.remove subId )}
                    return! loop newState
            }
        loop initState
    )

type Result<'s, 'f> =  
    | Success of 's
    | Failure of 'f

type EventStore<'event, 'error> =  
    { GetEvents: StreamId -> Result<StreamVersion*'event list, 'error>
    ; SaveEvents: StreamId -> StreamVersion -> 'event list -> Result<'event list, 'error>
    ; AddSubscriber: SubscriptionToken -> (StreamId * 'event list -> unit) -> unit
    ; RemoveSubscriber: SubscriptionToken -> unit
    }

let createEventStore<'event, 'error> (versionError:VersionConflict -> 'error) (agent: Agent<_>): EventStore<'event, 'error> =  
    let getEvents streamId : Result<StreamVersion*'event list, 'error> = 
        let result = (fun r -> GetEvents {StreamId=streamId; ReplyChannel=r }) |> agent.PostAndAsyncReply |> Async.RunSynchronously
        match result with
        | Some events -> Success (StreamVersion (events |> List.length), events)
        | None -> Success(StreamVersion 0, [])

    let saveEvents streamId expectedVersion events : Result<'event list, 'error> = 
        let result = (fun r -> 
            let batch = {StreamId = streamId; Events = events; ReplyChannel=r; StreamVersion = expectedVersion }
            SaveEvents batch) |> agent.PostAndAsyncReply |> Async.RunSynchronously
        match result with
        | VersionConflict vc -> Failure(versionError vc)
        | Ok                 -> Success events

    let addSubscriber subId subscriber = 
        agent.Post(AddSubscriber {SubscriptionToken = subId; EventHandler = subscriber })

    let removeSubscriber subId = 
        agent.Post(RemoveSubscriber subId)

    { GetEvents = getEvents; SaveEvents = saveEvents; AddSubscriber = addSubscriber; RemoveSubscriber = removeSubscriber}

let createInMemoryEventStore<'event, 'error> (versionError: VersionConflict -> 'error): EventStore<'event,'error> =  
    let initState : Map<StreamId, 'event list> = Map.empty

    let saveEventsInMap map (id: StreamId) expectedVersion (events: 'event list) = 
        match map |> Map.tryFind id with
        | None -> 
            (Ok, map |> Map.add id events)
        | Some existingEvents ->
            let currentVersion = StreamVersion(existingEvents |> List.length)
            match currentVersion = expectedVersion with
            | false -> 
                (VersionConflict {Expected = expectedVersion; Actual = currentVersion}, map)
            | true  -> 
                let updatedMap = map |> Map.add id (existingEvents@events)
                (Ok, updatedMap)

    let getEventsInMap (map: Map<StreamId, 'event list>) (id:StreamId) =
        Map.tryFind id map, map
    let agent: Agent<Messages<'event>> =
        createEventStoreAgent initState getEventsInMap saveEventsInMap
    createEventStore<'event, 'error> versionError agent